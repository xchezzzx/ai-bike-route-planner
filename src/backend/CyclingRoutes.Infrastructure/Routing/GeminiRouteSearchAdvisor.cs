using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Infrastructure.Interpretation;

namespace CyclingRoutes.Infrastructure.Routing;

public sealed class GeminiRouteSearchAdvisor(HttpClient client, GeminiOptions options, TimeProvider timeProvider) : IRouteSearchAdvisor
{
	private const int ResponseLimit = 256 * 1024;
	private static readonly JsonSerializerOptions InputJson = new(JsonSerializerDefaults.Web)
	{ Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) } };

	public async Task<RouteSearchAdvice> AdviseAsync(RouteSearchContext context, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (string.IsNullOrWhiteSpace(options.ApiKey) || options.ApiKey.Any(x => x < 33 || x > 126)
			|| string.IsNullOrEmpty(options.Model) || options.Model.Length > 128
			|| options.Model.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('.' or '-' or '_')))
			throw new RouteSearchAdvisorException(RouteSearchAdvisorFailure.NotConfigured);
		using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), timeProvider);
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
		using var request = new HttpRequestMessage(HttpMethod.Post,
			"https://generativelanguage.googleapis.com/v1beta/models/" + options.Model + ":generateContent");
		request.Headers.Add("x-goog-api-key", options.ApiKey);
		request.Content = JsonContent.Create(new
		{
			systemInstruction = new { parts = new[] { new { text = GeminiRouteSearchContract.SystemInstruction } } },
			contents = new[] { new { role = "user", parts = new[] { new { text = JsonSerializer.Serialize(context, InputJson) } } } },
			generationConfig = new { candidateCount = 1, maxOutputTokens = 1024, responseMimeType = "application/json", responseJsonSchema = GeminiRouteSearchContract.Schema }
		});
		try
		{
			linked.Token.ThrowIfCancellationRequested();
			using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
			linked.Token.ThrowIfCancellationRequested();
			if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new RouteSearchAdvisorException(RouteSearchAdvisorFailure.Authentication);
			if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new RouteSearchAdvisorException(RouteSearchAdvisorFailure.Quota);
			if ((int)response.StatusCode >= 500) throw new RouteSearchAdvisorException(RouteSearchAdvisorFailure.Unavailable);
			if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > ResponseLimit) throw new RouteSearchAdvisorException(RouteSearchAdvisorFailure.InvalidResponse);
			await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
			using var buffer = new MemoryStream();
			var chunk = new byte[8192];
			while (true)
			{
				linked.Token.ThrowIfCancellationRequested();
				var count = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, ResponseLimit + 1 - (int)buffer.Length)), linked.Token);
				if (count == 0) break;
				buffer.Write(chunk, 0, count);
				if (buffer.Length > ResponseLimit) throw new RouteSearchAdvisorException(RouteSearchAdvisorFailure.InvalidResponse);
			}
			linked.Token.ThrowIfCancellationRequested();
			var advice = GeminiRouteSearchParser.Parse(new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
			if (!RouteSearchProposalPolicy.IsValid(advice, context)) throw new RouteSearchAdvisorException(RouteSearchAdvisorFailure.InvalidResponse);
			linked.Token.ThrowIfCancellationRequested();
			return advice;
		}
		catch (Exception) when (cancellationToken.IsCancellationRequested) { cancellationToken.ThrowIfCancellationRequested(); throw; }
		catch (OperationCanceledException) { throw new RouteSearchAdvisorException(RouteSearchAdvisorFailure.Timeout); }
		catch (Exception error) when (error is HttpRequestException or IOException) { throw new RouteSearchAdvisorException(RouteSearchAdvisorFailure.Unavailable); }
		catch (DecoderFallbackException) { throw new RouteSearchAdvisorException(RouteSearchAdvisorFailure.InvalidResponse); }
	}
}
