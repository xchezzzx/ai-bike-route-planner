using System.Text.Json;
using CyclingRoutes.Application.Interpretation;

namespace CyclingRoutes.Infrastructure.Interpretation;

internal static class GeminiResponseParser
{
	public static RouteIntentExtraction Parse(string body)
	{
		try
		{
			using var json = JsonDocument.Parse(body);
			var root = json.RootElement;
			if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out var block)
				&& block.GetString() is "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST")
				throw new InterpretationException(InterpretationFailure.RequestRejected);
			var candidates = root.GetProperty("candidates");
			if (candidates.GetArrayLength() != 1) throw Invalid();
			var candidate = candidates[0];
			var finish = candidate.GetProperty("finishReason").GetString();
			if (finish is "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "SPII")
				throw new InterpretationException(InterpretationFailure.RequestRejected);
			if (finish != "STOP") throw Invalid();
			var parts = candidate.GetProperty("content").GetProperty("parts");
			if (parts.GetArrayLength() != 1) throw Invalid();
			var part = parts[0];
			if (part.TryGetProperty("thought", out var thought) && thought.ValueKind != JsonValueKind.False) throw Invalid();
			if (part.EnumerateObject().Any(x => x.Name is not ("text" or "thought" or "thoughtSignature"))) throw Invalid();
			using var extraction = JsonDocument.Parse(part.GetProperty("text").GetString()!);
			var data = extraction.RootElement;
			Closed(data, ["shape", "profile", "elevation", "targetDistanceMeters", "targetDurationSeconds", "issues"]);
			var issues = data.GetProperty("issues");
			if (issues.GetArrayLength() > 16) throw Invalid();
			var parsed = new List<ExtractionIssue>();
			foreach (var item in issues.EnumerateArray())
			{
				Closed(item, ["field", "code"]);
				var field = Token(item.GetProperty("field"), ["shape", "profile", "elevation", "targetDistanceMeters", "targetDurationSeconds", "start", "destination", "prompt"]);
				var code = Token(item.GetProperty("code"), ["ambiguous", "invalid_value", "location_requires_map_selection", "unsupported_preference"]);
				if (field is null || code is null) throw Invalid();
				parsed.Add(new(field, code));
			}
			var distance = data.GetProperty("targetDistanceMeters");
			double? meters = distance.ValueKind == JsonValueKind.Null ? null : distance.GetDouble();
			if (meters is { } number && !double.IsFinite(number)) throw Invalid();
			var duration = data.GetProperty("targetDurationSeconds");
			long? seconds = duration.ValueKind == JsonValueKind.Null ? null : duration.GetInt64();
			return new(Token(data.GetProperty("shape"), ["loop", "pointToPoint"]),
				Token(data.GetProperty("profile"), ["road", "gravel"]),
				Token(data.GetProperty("elevation"), ["minimize", "balanced", "seekClimbs"]),
				meters, seconds, parsed.Distinct().ToArray());
		}
		catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException or OverflowException)
		{
			throw Invalid();
		}
	}

	private static void Closed(JsonElement value, string[] names)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var property in value.EnumerateObject())
			if (!names.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw Invalid();
		if (seen.Count != names.Length) throw Invalid();
	}

	private static string? Token(JsonElement value, string[] allowed)
	{
		if (value.ValueKind == JsonValueKind.Null) return null;
		var token = value.GetString();
		if (token is null || !allowed.Contains(token, StringComparer.Ordinal)) throw Invalid();
		return token;
	}
	private static InterpretationException Invalid() => new(InterpretationFailure.InvalidResponse);
}
