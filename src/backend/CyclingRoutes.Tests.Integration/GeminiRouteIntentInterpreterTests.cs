using System.Net;
using System.Text;
using System.Text.Json;
using CyclingRoutes.Application.Interpretation;
using CyclingRoutes.Infrastructure.Interpretation;

namespace CyclingRoutes.Tests.Integration;

public class GeminiRouteIntentInterpreterTests
{
	public const string Extraction = """{"shape":"loop","profile":"road","elevation":null,"targetDistanceMeters":20000,"targetDurationSeconds":null,"issues":[]}""";
	public static string Envelope(string extraction = Extraction, string finish = "STOP") => JsonSerializer.Serialize(new
	{
		candidates = new[] { new { finishReason = finish, content = new { parts = new[] { new { text = extraction } } } } },
		modelVersion = "test-model", usageMetadata = new { promptTokenCount = 42 }
	});
	private static GeminiOptions Options => new() { ApiKey = "private-test-key", Model = "test-model" };
	public sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
	}
	public static HttpResponseMessage Response(string body, int status = 200) => new((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
	private static async Task<RouteIntentExtraction> Call(string body, int status = 200)
	{
		using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Response(body, status))));
		return await new GeminiRouteIntentInterpreter(client, Options, TimeProvider.System).InterpretAsync("ride", "en", TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task Request_UsesOneStructuredCallWithoutCoordinatesOrQueryKey()
	{
		var calls = 0;
		using var client = new HttpClient(new Handler(async (request, ct) =>
		{
			calls++;
			Assert.Equal(HttpMethod.Post, request.Method);
			Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/test-model:generateContent", request.RequestUri!.AbsoluteUri);
			Assert.Equal("private-test-key", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
			using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
			var root = json.RootElement;
			Assert.False(root.TryGetProperty("tools", out _));
			using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "prompt-interpretation-v1.json")));
			Assert.Equal("prompt-interpretation-v3", corpus.RootElement.GetProperty("contractVersion").GetString());
			Assert.Contains(corpus.RootElement.GetProperty("contractVersion").GetString()!, root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
			Assert.Contains("References to selected points", root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
			Assert.Contains("Endpoint letters A/B", root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
			Assert.Contains("Do not emit invalid_value for an explicit zero or negative target", root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
			Assert.Equal("user", root.GetProperty("contents")[0].GetProperty("role").GetString());
			Assert.Contains("ride", root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
			var config = root.GetProperty("generationConfig");
			Assert.Equal(1, config.GetProperty("candidateCount").GetInt32());
			Assert.Equal(4096, config.GetProperty("maxOutputTokens").GetInt32());
			Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
			var schema = config.GetProperty("responseJsonSchema");
			Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
			Assert.Equal(6, schema.GetProperty("required").GetArrayLength());
			Assert.False(schema.GetProperty("properties").TryGetProperty("start", out _));
			return Response(Envelope());
		}));
		var result = await new GeminiRouteIntentInterpreter(client, Options, TimeProvider.System).InterpretAsync("ride", "ru", TestContext.Current.CancellationToken);
		Assert.Equal(20000, result.TargetDistanceMeters);
		Assert.Equal("loop", result.Shape);
		Assert.Empty(result.Issues);
		Assert.Equal(1, calls);
	}

	[Theory]
	[InlineData("", "test")]
	[InlineData("key", "")]
	[InlineData("key", "../test")]
	[InlineData("key", "test?key=private")]
	[InlineData("key\r\ninjection", "test")]
	public async Task InvalidConfig_DoesNotCallNetwork(string key, string model)
	{
		using var client = new HttpClient(new Handler((_, _) => throw new Exception("network must not be called")));
		var error = await Assert.ThrowsAsync<InterpretationException>(() => new GeminiRouteIntentInterpreter(client,
			new() { ApiKey = key, Model = model }, TimeProvider.System).InterpretAsync("ride", "en", TestContext.Current.CancellationToken));
		Assert.Equal(InterpretationFailure.NotConfigured, error.Failure);
	}

	[Theory]
	[InlineData(401, InterpretationFailure.CredentialsRejected)]
	[InlineData(403, InterpretationFailure.CredentialsRejected)]
	[InlineData(429, InterpretationFailure.RateLimited)]
	[InlineData(500, InterpretationFailure.Unavailable)]
	[InlineData(503, InterpretationFailure.Unavailable)]
	[InlineData(400, InterpretationFailure.InvalidResponse)]
	[InlineData(404, InterpretationFailure.InvalidResponse)]
	[InlineData(302, InterpretationFailure.InvalidResponse)]
	public async Task HttpErrors_AreSafe(int status, InterpretationFailure failure)
	{
		var error = await Assert.ThrowsAsync<InterpretationException>(() => Call("private provider detail", status));
		Assert.Equal(failure, error.Failure);
		Assert.DoesNotContain("private", error.ToString());
		Assert.Null(error.InnerException);
	}

	public static IEnumerable<object[]> MalformedExtractions()
	{
		yield return ["{}"]; yield return ["null"]; yield return ["```json\n" + Extraction + "\n```"];
		yield return [Extraction.Replace("20000", "\"20000\"")];
		yield return [Extraction.Replace("20000", "1e999")];
		yield return [Extraction.Replace("\"loop\"", "\"triangle\"")];
		yield return [Extraction.Replace("\"issues\":[]", "\"issues\":[],\"start\":{}")] ;
		yield return [Extraction.Replace("\"issues\":[]", "\"issues\":[],\"shape\":\"loop\"")];
		yield return [Extraction.Replace("\"targetDurationSeconds\":null", "\"targetDurationSeconds\":1.5")];
		yield return [Extraction.Replace("\"targetDurationSeconds\":null", "\"targetDurationSeconds\":9223372036854775808")];
		yield return [Extraction.Replace("[]", "[{\"field\":\"prompt\",\"code\":\"unknown\"}]")];
		yield return [Extraction.Replace("[]", "[{\"field\":\"prompt\",\"field\":\"prompt\",\"code\":\"ambiguous\"}]")];
		yield return [Extraction.Replace("[]", "[{\"field\":\"prompt\",\"code\":\"ambiguous\",\"extra\":1}]")];
		yield return [Extraction.Replace("\"elevation\":null,", "")];
	}
	[Theory, MemberData(nameof(MalformedExtractions))]
	public async Task StrictExtraction_RejectsMalformedData(string extraction)
	{
		var error = await Assert.ThrowsAsync<InterpretationException>(() => Call(Envelope(extraction)));
		Assert.Equal(InterpretationFailure.InvalidResponse, error.Failure);
	}

	[Theory]
	[InlineData("{}")]
	[InlineData("{\"candidates\":[]}")]
	[InlineData("{\"candidates\":[{},{}]}")]
	[InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[]}}]}")]
	public async Task IncompleteEnvelope_IsNotSuccess(string body) =>
		Assert.Equal(InterpretationFailure.InvalidResponse, (await Assert.ThrowsAsync<InterpretationException>(() => Call(body))).Failure);

	[Theory]
	[InlineData("SAFETY", InterpretationFailure.RequestRejected)]
	[InlineData("PROHIBITED_CONTENT", InterpretationFailure.RequestRejected)]
	[InlineData("MAX_TOKENS", InterpretationFailure.InvalidResponse)]
	[InlineData("OTHER", InterpretationFailure.InvalidResponse)]
	public async Task FinishReason_IsEnforced(string reason, InterpretationFailure failure) =>
		Assert.Equal(failure, (await Assert.ThrowsAsync<InterpretationException>(() => Call(Envelope(finish: reason)))).Failure);

	[Fact]
	public async Task PromptSafetyBlock_IsRejection() => Assert.Equal(InterpretationFailure.RequestRejected,
		(await Assert.ThrowsAsync<InterpretationException>(() => Call("{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}"))).Failure);

	[Theory]
	[InlineData(16)]
	[InlineData(17)]
	public async Task IssueBudget_IsEnforcedBeforeDeduplication(int count)
	{
		var list = JsonSerializer.Serialize(Enumerable.Repeat(new { field = "prompt", code = "ambiguous" }, count));
		var body = Envelope(Extraction.Replace("[]", list));
		if (count == 16) Assert.Single((await Call(body)).Issues);
		else Assert.Equal(InterpretationFailure.InvalidResponse, (await Assert.ThrowsAsync<InterpretationException>(() => Call(body))).Failure);
	}

	[Fact]
	public async Task NegativeTarget_IsPreservedForDomainValidation() => Assert.Equal(-1, (await Call(Envelope(Extraction.Replace("20000", "-1")))).TargetDistanceMeters);

	[Theory]
	[InlineData(262144, false)]
	[InlineData(262145, false)]
	[InlineData(262145, true)]
	public async Task BodyLimit_CoversStreamingWithoutContentLength(int size, bool knownLength)
	{
		var body = Envelope().PadRight(size);
		using var content = knownLength ? (HttpContent)new StringContent(body) : new StreamContent(new UnseekableStream(Encoding.UTF8.GetBytes(body)));
		using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content })));
		var service = new GeminiRouteIntentInterpreter(client, Options, TimeProvider.System);
		if (size == 262144) Assert.Equal("road", (await service.InterpretAsync("ride", "en", TestContext.Current.CancellationToken)).Profile);
		else Assert.Equal(InterpretationFailure.InvalidResponse, (await Assert.ThrowsAsync<InterpretationException>(() => service.InterpretAsync("ride", "en", TestContext.Current.CancellationToken))).Failure);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Deadline_CoversStalledBodyAndCallerWins(bool cancelCaller)
	{
		using var caller = new CancellationTokenSource();
		var clock = new ManualClock();
		using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StreamContent(new StalledStream(() => { if (cancelCaller) caller.Cancel(); clock.Fire(); }))
		}))) { Timeout = Timeout.InfiniteTimeSpan };
		var task = new GeminiRouteIntentInterpreter(client, Options, clock).InterpretAsync("ride", "en", caller.Token);
		if (cancelCaller) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
		else Assert.Equal(InterpretationFailure.Timeout, (await Assert.ThrowsAsync<InterpretationException>(() => task)).Failure);
	}

	[Fact]
	public async Task TransportError_IsUnavailable()
	{
		using var client = new HttpClient(new Handler((_, _) => throw new HttpRequestException("private")));
		Assert.Equal(InterpretationFailure.Unavailable, (await Assert.ThrowsAsync<InterpretationException>(() =>
			new GeminiRouteIntentInterpreter(client, Options, TimeProvider.System).InterpretAsync("ride", "en", TestContext.Current.CancellationToken))).Failure);
	}

	private class UnseekableStream(byte[] bytes) : MemoryStream(bytes) { public override bool CanSeek => false; }
	private sealed class StalledStream(Action onRead) : UnseekableStream([])
	{
		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			onRead(); await Task.Delay(Timeout.Infinite, cancellationToken); return 0;
		}
	}
	private sealed class ManualClock : TimeProvider
	{
		private Action? _fire;
		public void Fire() => _fire!();
		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			Assert.Equal(TimeSpan.FromSeconds(30), dueTime);
			_fire = () => callback(state); return new Timer();
		}
		private sealed class Timer : ITimer
		{
			public bool Change(TimeSpan dueTime, TimeSpan period) => true;
			public void Dispose() { }
			public ValueTask DisposeAsync() => ValueTask.CompletedTask;
		}
	}
}
