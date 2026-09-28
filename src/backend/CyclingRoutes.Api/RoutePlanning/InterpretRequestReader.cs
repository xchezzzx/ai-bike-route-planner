using System.Net.Http.Headers;
using System.Text.Json;
using CyclingRoutes.Contracts.RoutePlanning;
using Microsoft.Extensions.Options;

namespace CyclingRoutes.Api.RoutePlanning;

internal sealed record InterpretRequestReadResult(InterpretRouteIntentRequest? Request, int? ErrorStatus);
internal static class InterpretRequestReader
{
	public static async Task<InterpretRequestReadResult> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
	{
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
			var result = JsonSerializer.Deserialize<InterpretRouteIntentRequest>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), options);
			return result is null ? new(null, 400) : new(result, null);
		}
		catch (JsonException) { return new(null, 400); }
	}
}
