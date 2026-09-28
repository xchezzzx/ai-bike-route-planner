using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CyclingRoutes.Application.Interpretation;

namespace CyclingRoutes.Infrastructure.Interpretation;

public sealed class GeminiRouteIntentInterpreter(HttpClient client, GeminiOptions options, TimeProvider timeProvider) : IRouteIntentInterpreter
{
	private const int ResponseLimit = 256 * 1024;
	public async Task<RouteIntentExtraction> InterpretAsync(string prompt, string locale, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (string.IsNullOrWhiteSpace(options.ApiKey) || options.ApiKey.Any(x => x < 33 || x > 126)
			|| string.IsNullOrEmpty(options.Model) || options.Model.Length > 128
			|| options.Model.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('.' or '-' or '_')))
			throw new InterpretationException(InterpretationFailure.NotConfigured);
		using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), timeProvider);
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
		using var request = new HttpRequestMessage(HttpMethod.Post,
			"https://generativelanguage.googleapis.com/v1beta/models/" + options.Model + ":generateContent");
		request.Headers.Add("x-goog-api-key", options.ApiKey);
		request.Content = JsonContent.Create(new
		{
			systemInstruction = new { parts = new[] { new { text = GeminiExtractionContract.SystemInstruction } } },
			contents = new[] { new { role = "user", parts = new[] { new { text = JsonSerializer.Serialize(new { prompt, locale }) } } } },
			generationConfig = new { candidateCount = 1, maxOutputTokens = 4096,
				responseMimeType = "application/json", responseJsonSchema = GeminiExtractionContract.Schema }
		});
		try
		{
			using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
			cancellationToken.ThrowIfCancellationRequested();
			if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
				throw new InterpretationException(InterpretationFailure.CredentialsRejected);
			if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new InterpretationException(InterpretationFailure.RateLimited);
			if ((int)response.StatusCode >= 500) throw new InterpretationException(InterpretationFailure.Unavailable);
			if (!response.IsSuccessStatusCode) throw new InterpretationException(InterpretationFailure.InvalidResponse);
			if (response.Content.Headers.ContentLength > ResponseLimit) throw new InterpretationException(InterpretationFailure.InvalidResponse);
			await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
			using var buffer = new MemoryStream();
			var chunk = new byte[8192];
			while (true)
			{
				var count = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, ResponseLimit + 1 - (int)buffer.Length)), linked.Token);
				if (count == 0) break;
				buffer.Write(chunk, 0, count);
				if (buffer.Length > ResponseLimit) throw new InterpretationException(InterpretationFailure.InvalidResponse);
			}
			var result = GeminiResponseParser.Parse(new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
			cancellationToken.ThrowIfCancellationRequested();
			linked.Token.ThrowIfCancellationRequested();
			return result;
		}
		catch (Exception) when (cancellationToken.IsCancellationRequested) { cancellationToken.ThrowIfCancellationRequested(); throw; }
		catch (OperationCanceledException) { throw new InterpretationException(InterpretationFailure.Timeout); }
		catch (Exception error) when (error is HttpRequestException or IOException)
		{
			throw new InterpretationException(InterpretationFailure.Unavailable);
		}
		catch (DecoderFallbackException) { throw new InterpretationException(InterpretationFailure.InvalidResponse); }
	}
}
