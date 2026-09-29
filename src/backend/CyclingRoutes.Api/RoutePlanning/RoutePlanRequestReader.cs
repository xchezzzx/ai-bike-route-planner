using System.Net.Http.Headers;
using System.Text.Json;
using CyclingRoutes.Contracts.RoutePlanning;
using Microsoft.Extensions.Options;

namespace CyclingRoutes.Api.RoutePlanning;

internal sealed record RoutePlanRequestReadResult(RouteIntentRequest? Request, int? ErrorStatus);

internal static class RoutePlanRequestReader
{
	public static async Task<RoutePlanRequestReadResult> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		const int limit = 64 * 1024;
		if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var media)
			|| !(string.Equals(media.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)
				|| (media.MediaType?.StartsWith("application/", StringComparison.OrdinalIgnoreCase) == true
					&& media.MediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
			|| (media.CharSet is { } charset && !string.Equals(charset.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase)))
			return new(null, 415);
		if (request.ContentLength > limit) return new(null, 413);
		using var buffer = new MemoryStream();
		var chunk = new byte[8192];
		while (true)
		{
			var count = await request.Body.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, limit + 1 - (int)buffer.Length)), cancellationToken);
			if (count == 0) break;
			buffer.Write(chunk, 0, count);
			if (buffer.Length > limit) return new(null, 413);
		}
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			var options = request.HttpContext.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
			using var document = JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
			if (!HasUniqueProperties(document.RootElement)) return new(null, 400);
			var result = document.RootElement.Deserialize<RouteIntentRequest>(options);
			return result is null ? new(null, 400) : new(result, null);
		}
		catch (JsonException) { return new(null, 400); }
	}

	private static bool HasUniqueProperties(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var property in element.EnumerateObject())
				if (!names.Add(property.Name) || !HasUniqueProperties(property.Value)) return false;
		}
		else if (element.ValueKind == JsonValueKind.Array)
			foreach (var item in element.EnumerateArray())
				if (!HasUniqueProperties(item)) return false;
		return true;
	}
}
