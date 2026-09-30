using CyclingRoutes.Application.RoutePlanning;
using CyclingRoutes.Contracts.RoutePlanning;

namespace CyclingRoutes.Application.Interpretation;

public sealed class InterpretationService(IRouteIntentInterpreter interpreter, RouteIntentValidator validator)
{
	private static readonly string[] FieldOrder = ["start", "shape", "profile", "destination", "targetDistanceMeters", "targetDistanceRangeMeters", "targetDurationSeconds", "targetDurationRangeSeconds", "elevation", "prompt"];

	public async Task<InterpretationResult> InterpretAsync(InterpretRouteIntentRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var errors = new Dictionary<string, string[]>();
		if (string.IsNullOrWhiteSpace(request.Prompt)) errors["prompt"] = ["required"];
		else if (request.Prompt.Length > 4000) errors["prompt"] = ["too_long"];
		if (request.Locale is not ("en" or "he" or "ru")) errors["locale"] = ["invalid_value"];
		ValidateCoordinate(request.Start, "start", errors);
		ValidateCoordinate(request.Destination, "destination", errors);
		if (errors.Count > 0) return new(null, errors);

		var extracted = await interpreter.InterpretAsync(request.Prompt!, request.Locale!, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		var issues = extracted.Issues.Distinct().ToArray();
		var assumptions = new List<string>();
		var defaultElevation = extracted.Elevation is null && !issues.Any(x => x.Field == "elevation");
		if (defaultElevation)
			assumptions.Add("elevation_balanced");
		var draft = new RouteIntentRequest
		{
			Start = request.Start, Destination = request.Destination, Shape = extracted.Shape,
			Profile = extracted.Profile, Elevation = extracted.Elevation ?? (defaultElevation ? "balanced" : null),
			TargetDistanceMeters = extracted.TargetDistanceMeters, TargetDurationSeconds = extracted.TargetDurationSeconds,
			TargetDistanceRangeMeters = extracted.TargetDistanceRangeMeters, TargetDurationRangeSeconds = extracted.TargetDurationRangeSeconds
		};
		var blocked = issues.Where(x => x.Code != "unsupported_preference").Select(x => x.Field).ToHashSet();
		// Preserve the user's draft, but validate only unambiguous extracted preferences.
		var candidate = draft with
		{
			Shape = blocked.Contains("shape") ? null : draft.Shape,
			Profile = blocked.Contains("profile") ? null : draft.Profile,
			Elevation = blocked.Contains("elevation") ? null : draft.Elevation,
			TargetDistanceMeters = blocked.Contains("targetDistanceMeters") ? null : draft.TargetDistanceMeters,
			TargetDurationSeconds = blocked.Contains("targetDurationSeconds") ? null : draft.TargetDurationSeconds,
			TargetDistanceRangeMeters = blocked.Contains("targetDistanceRangeMeters") ? null : draft.TargetDistanceRangeMeters,
			TargetDurationRangeSeconds = blocked.Contains("targetDurationRangeSeconds") ? null : draft.TargetDurationRangeSeconds
		};
		var validation = validator.Validate(candidate, cancellationToken);
		var questions = issues.Where(x => x.Code != "unsupported_preference").ToList();
		foreach (var (field, codes) in validation.Errors)
		foreach (var code in codes)
		{
			if (code == "required" && blocked.Contains(field)) continue;
			if (code == "target_required")
			{
				if (field == "targetDurationSeconds" || blocked.Overlaps(["targetDistanceMeters", "targetDurationSeconds", "targetDistanceRangeMeters", "targetDurationRangeSeconds"])) continue;
			}
			questions.Add(new(field, code));
		}
		var limitations = new List<string>();
		if (issues.Any(x => x.Code == "unsupported_preference")) limitations.Add("unsupported_preference");
		if (candidate.Profile == "gravel") limitations.Add("gravel_not_supported");
		if (candidate.Shape == "pointToPoint" && candidate.Elevation is "minimize" or "seekClimbs")
			limitations.Add("point_to_point_elevation_not_supported");
		if (candidate.Shape == "loop" && candidate.Profile == "road"
			&& !blocked.Overlaps(["targetDistanceMeters", "targetDistanceRangeMeters"]))
		{
			// Capability limits depend on known search parameters, not unrelated missing fields.
			double? length = null;
			if (candidate.TargetDistanceMeters is { } meters && double.IsFinite(meters) && meters > 0)
				length = meters;
			else if (candidate.TargetDistanceRangeMeters is { Min: { } min, Max: { } max }
				&& double.IsFinite(min) && double.IsFinite(max) && min > 0 && min <= max)
				length = min + (max - min) / 2;
			else if (candidate.TargetDistanceMeters is null && candidate.TargetDistanceRangeMeters is null)
			{
				if (candidate.TargetDurationSeconds is > 0 && candidate.TargetDurationSeconds <= TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond)
					length = (double)candidate.TargetDurationSeconds.Value * 20000 / 3600;
				else if (candidate.TargetDurationRangeSeconds is { Min: > 0, Max: { } upper } range
					&& range.Min <= upper && upper <= TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond)
					length = (range.Min.Value + (upper - range.Min.Value) / 2d) * 20000 / 3600;
			}
			if (length is < 1000 or > 100000) limitations.Add("loop_search_distance_out_of_range");
		}
		var clarifications = questions.Distinct().OrderBy(x => Array.IndexOf(FieldOrder, x.Field.Split('.')[0]))
			.ThenBy(x => x.Field, StringComparer.Ordinal).ThenBy(x => x.Code, StringComparer.Ordinal)
			.Select(x => new ClarificationResponse(x.Field, x.Code, ClarificationMessages.Get(request.Locale!, x.Field, x.Code))).ToArray();
		RouteIntentResponse? responseIntent = null;
		if (validation.Intent is { } valid && clarifications.Length == 0 && issues.Length == 0)
			responseIntent = new(new(valid.Start.Latitude, valid.Start.Longitude),
				valid.Destination is { } destination ? new(destination.Latitude, destination.Longitude) : null,
				draft.Shape!, draft.Profile!, draft.Elevation!, draft.TargetDistanceMeters, draft.TargetDurationSeconds,
				valid.TargetDistanceRange is { } dr ? new(dr.Min, dr.Max) : null,
				valid.TargetDurationRange is { } tr ? new(tr.Min, tr.Max) : null);
		cancellationToken.ThrowIfCancellationRequested();
		var status = clarifications.Length > 0 ? "needsClarification" : limitations.Count > 0 ? "unsupported" : "ready";
		return new(new(status, draft, responseIntent, clarifications, limitations, assumptions), errors);
	}

	private static void ValidateCoordinate(CoordinateRequest? coordinate, string field, Dictionary<string, string[]> errors)
	{
		if (coordinate is null) return;
		Check(coordinate.Latitude, -90, 90, field + ".latitude");
		Check(coordinate.Longitude, -180, 180, field + ".longitude");
		void Check(double? value, double min, double max, string key)
		{
			if (value is null) errors[key] = ["required"];
			else if (!double.IsFinite(value.Value) || value < min || value > max) errors[key] = ["out_of_range"];
		}
	}
}
