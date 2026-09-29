using System.Text.Json;
using CyclingRoutes.Application.Routing;

namespace CyclingRoutes.Infrastructure.Routing;

internal static class GeminiRouteSearchParser
{
	public static RouteSearchAdvice Parse(string responseJson)
	{
		try
		{
			using var envelope = JsonDocument.Parse(responseJson);
			UniqueProperties(envelope.RootElement);
			var root = envelope.RootElement;
			if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out _)) throw Invalid();
			var candidates = root.GetProperty("candidates");
			if (candidates.GetArrayLength() != 1) throw Invalid();
			var candidate = candidates[0];
			if (candidate.GetProperty("finishReason").GetString() != "STOP") throw Invalid();
			if (candidate.TryGetProperty("safetyRatings", out var ratings)
				&& ratings.EnumerateArray().Any(x => x.TryGetProperty("blocked", out var blocked) && blocked.GetBoolean())) throw Invalid();
			var parts = candidate.GetProperty("content").GetProperty("parts");
			if (parts.GetArrayLength() != 1) throw Invalid();
			var part = parts[0];
			if (part.TryGetProperty("thought", out var thought) && thought.ValueKind != JsonValueKind.False) throw Invalid();
			if (part.EnumerateObject().Any(x => x.Name is not ("text" or "thought" or "thoughtSignature"))) throw Invalid();
			using var advice = JsonDocument.Parse(part.GetProperty("text").GetString()!);
			var data = advice.RootElement;
			UniqueProperties(data);
			string[] fields = ["action", "seed", "requestedLengthMeters", "reason"];
			if (data.EnumerateObject().Count() != fields.Length || data.EnumerateObject().Any(x => !fields.Contains(x.Name))) throw Invalid();
			var action = data.GetProperty("action").GetString() switch { "search" => RouteSearchAction.Search, "stop" => RouteSearchAction.Stop, _ => throw Invalid() };
			var reason = data.GetProperty("reason").GetString() switch
			{
				"distance" => RouteSearchReason.Distance, "duration" => RouteSearchReason.Duration, "elevation" => RouteSearchReason.Elevation,
				"explore" => RouteSearchReason.Explore, "stop" => RouteSearchReason.Stop, _ => throw Invalid()
			};
			var seed = data.GetProperty("seed");
			var length = data.GetProperty("requestedLengthMeters");
			return new(action, seed.ValueKind == JsonValueKind.Null ? null : seed.GetInt32(),
				length.ValueKind == JsonValueKind.Null ? null : length.GetDouble(), reason);
		}
		catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException or OverflowException)
		{ throw Invalid(); }
	}

	private static void UniqueProperties(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			var names = new HashSet<string>(StringComparer.Ordinal);
			foreach (var property in element.EnumerateObject())
			{ if (!names.Add(property.Name)) throw Invalid(); UniqueProperties(property.Value); }
		}
		else if (element.ValueKind == JsonValueKind.Array)
			foreach (var value in element.EnumerateArray()) UniqueProperties(value);
	}
	private static RouteSearchAdvisorException Invalid() => new(RouteSearchAdvisorFailure.InvalidResponse);
}
