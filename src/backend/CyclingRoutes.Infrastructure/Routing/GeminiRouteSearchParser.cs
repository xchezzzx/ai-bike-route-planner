using System.Text.Json;
using CyclingRoutes.Application.Routing;

namespace CyclingRoutes.Infrastructure.Routing;

internal static class GeminiRouteSearchParser
{
	public static RouteSearchAdvice Parse(string responseJson)
	{
		var stage = RouteSearchAdvisorDiagnostic.MalformedEnvelope;
		try
		{
			using var envelope = JsonDocument.Parse(responseJson);
			UniqueProperties(envelope.RootElement);
			var root = envelope.RootElement;
			if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out _)) throw Invalid(RouteSearchAdvisorDiagnostic.PromptBlocked);
			var candidates = root.GetProperty("candidates");
			if (candidates.GetArrayLength() != 1) throw Invalid();
			var candidate = candidates[0];
			var finish = candidate.GetProperty("finishReason").GetString();
			if (finish != "STOP") throw Invalid(finish == "MAX_TOKENS"
				? RouteSearchAdvisorDiagnostic.OutputTokenLimit : RouteSearchAdvisorDiagnostic.IncompleteCandidate);
			if (candidate.TryGetProperty("safetyRatings", out var ratings)
				&& ratings.EnumerateArray().Any(x => x.TryGetProperty("blocked", out var blocked) && blocked.GetBoolean())) throw Invalid(RouteSearchAdvisorDiagnostic.PromptBlocked);
			stage = RouteSearchAdvisorDiagnostic.InvalidParts;
			var parts = candidate.GetProperty("content").GetProperty("parts");
			if (parts.GetArrayLength() != 1) throw Invalid(stage);
			var part = parts[0];
			if (part.TryGetProperty("thought", out var thought) && thought.ValueKind != JsonValueKind.False) throw Invalid(stage);
			if (part.EnumerateObject().Any(x => x.Name is not ("text" or "thought" or "thoughtSignature"))) throw Invalid(stage);
			stage = RouteSearchAdvisorDiagnostic.InvalidAdviceJson;
			using var advice = JsonDocument.Parse(part.GetProperty("text").GetString()!);
			var data = advice.RootElement;
			stage = RouteSearchAdvisorDiagnostic.InvalidAdviceFields;
			UniqueProperties(data, stage);
			if (!data.TryGetProperty("nextSearch", out var next)) throw Invalid(RouteSearchAdvisorDiagnostic.MissingAdviceFields);
			if (data.EnumerateObject().Count() != 1) throw Invalid(stage);
			if (next.ValueKind == JsonValueKind.Null) return new(RouteSearchAction.Stop, null, null, RouteSearchReason.Stop);
			data = next;
			string[] fields = ["seed", "requestedLengthMeters", "reason"];
			if (fields.Any(field => !data.TryGetProperty(field, out _))) throw Invalid(RouteSearchAdvisorDiagnostic.MissingAdviceFields);
			if (data.EnumerateObject().Count() != fields.Length || data.EnumerateObject().Any(x => !fields.Contains(x.Name))) throw Invalid(stage);
			stage = RouteSearchAdvisorDiagnostic.InvalidAdviceReason;
			var reason = data.GetProperty("reason").GetString() switch
			{
				"distance" => RouteSearchReason.Distance, "duration" => RouteSearchReason.Duration, "elevation" => RouteSearchReason.Elevation,
				"explore" => RouteSearchReason.Explore, _ => throw Invalid(stage)
			};
			var seed = data.GetProperty("seed");
			var length = data.GetProperty("requestedLengthMeters");
			stage = RouteSearchAdvisorDiagnostic.InvalidAdviceSeed;
			var parsedSeed = seed.GetInt32();
			stage = RouteSearchAdvisorDiagnostic.InvalidAdviceLength;
			var parsedLength = length.GetDouble();
			return new(RouteSearchAction.Search, parsedSeed, parsedLength, reason);
		}
		catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException or OverflowException)
		{ throw Invalid(stage); }
	}

	private static void UniqueProperties(JsonElement element, RouteSearchAdvisorDiagnostic diagnostic = RouteSearchAdvisorDiagnostic.MalformedEnvelope)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			var names = new HashSet<string>(StringComparer.Ordinal);
			foreach (var property in element.EnumerateObject())
			{ if (!names.Add(property.Name)) throw Invalid(diagnostic); UniqueProperties(property.Value, diagnostic); }
		}
		else if (element.ValueKind == JsonValueKind.Array)
			foreach (var value in element.EnumerateArray()) UniqueProperties(value, diagnostic);
	}
	private static RouteSearchAdvisorException Invalid(RouteSearchAdvisorDiagnostic diagnostic = RouteSearchAdvisorDiagnostic.MalformedEnvelope)
		=> new(RouteSearchAdvisorFailure.InvalidResponse, diagnostic);
}
