using System.Net;
using System.Text;
using System.Text.Json;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;
using CyclingRoutes.Infrastructure.Interpretation;
using CyclingRoutes.Infrastructure.Routing;
using static CyclingRoutes.Tests.Integration.GeminiRouteIntentInterpreterTests;

namespace CyclingRoutes.Tests.Integration;

public class GeminiRouteSearchAdvisorTests
{
	[Fact]
	public async Task SerializedRangePreferencesKeepBoundsWithoutComputedMidpoints()
	{
		var context = Context with { Preferences = Context.Preferences with
		{
			TargetDistanceMeters = null, TargetDurationSeconds = null,
			TargetDistanceRangeMeters = new(18000, 22000), TargetDurationRangeSeconds = new(3000, 4200)
		} };
		using var client = new HttpClient(new Handler(async (request, ct) =>
		{
			using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
			using var input = JsonDocument.Parse(json.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!);
			var preferences = input.RootElement.GetProperty("preferences");
			Assert.Equal(JsonValueKind.Null, preferences.GetProperty("targetDistanceMeters").ValueKind);
			Assert.Equal(JsonValueKind.Null, preferences.GetProperty("targetDurationSeconds").ValueKind);
			var distance = preferences.GetProperty("targetDistanceRangeMeters");
			var duration = preferences.GetProperty("targetDurationRangeSeconds");
			Assert.Equal(new[] { "min", "max" }, distance.EnumerateObject().Select(x => x.Name));
			Assert.Equal(new[] { "min", "max" }, duration.EnumerateObject().Select(x => x.Name));
			Assert.Equal(18000, distance.GetProperty("min").GetDouble());
			Assert.Equal(22000, distance.GetProperty("max").GetDouble());
			Assert.Equal(3000, duration.GetProperty("min").GetInt64());
			Assert.Equal(4200, duration.GetProperty("max").GetInt64());
			return Response(Envelope(Stop));
		}));
		Assert.Equal(RouteSearchAction.Stop, (await new GeminiRouteSearchAdvisor(client, Options, TimeProvider.System)
			.AdviseAsync(context, TestContext.Current.CancellationToken)).Action);
	}

	public const string Search = """{"nextSearch":{"seed":7,"requestedLengthMeters":16000,"reason":"distance"}}""";
	public const string Stop = """{"nextSearch":null}""";
	public static RouteSearchContext Context => new(new(RouteShape.Loop, CyclingProfile.Road, ElevationPreference.Balanced, 20000, null),
		20000, [new(1, 20000, RouteSearchOutcome.Accepted, 28000, 5000, 100, 8000, null), new(2, 20000, RouteSearchOutcome.NoRoute, null, null, null, null, null)]);
	private static GeminiOptions Options => new() { ApiKey = "private-test-key", Model = "test-model" };
	private static async Task<RouteSearchAdvice> Call(string body, int status = 200)
	{
		using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Response(body, status))));
		return await new GeminiRouteSearchAdvisor(client, Options, TimeProvider.System).AdviseAsync(Context, TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task SerializedRequestContainsOnlyPreferencesAndObservations()
	{
		var calls = 0;
		using var client = new HttpClient(new Handler(async (request, ct) =>
		{
			calls++;
			Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/test-model:generateContent", request.RequestUri!.AbsoluteUri);
			Assert.Equal("private-test-key", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
			var body = await request.Content!.ReadAsStringAsync(ct);
			Assert.DoesNotContain("private-test-key", body);
			using var json = JsonDocument.Parse(body);
			var root = json.RootElement;
			Assert.Equal(new[] { "systemInstruction", "contents", "generationConfig" }, root.EnumerateObject().Select(x => x.Name));
			using var corpus = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "route-refinement-v2.json")));
			Assert.Equal("route-search-v3", corpus.RootElement.GetProperty("contractVersion").GetString());
			Assert.Contains(corpus.RootElement.GetProperty("contractVersion").GetString()!, root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
			using var input = JsonDocument.Parse(root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!);
			Assert.Equal(new[] { "preferences", "initialLengthMeters", "observations" }, input.RootElement.EnumerateObject().Select(x => x.Name));
			Assert.Equal(new[] { "shape", "profile", "elevation", "targetDistanceMeters", "targetDurationSeconds", "targetDistanceRangeMeters", "targetDurationRangeSeconds" }, input.RootElement.GetProperty("preferences").EnumerateObject().Select(x => x.Name));
			Assert.Equal("road", input.RootElement.GetProperty("preferences").GetProperty("profile").GetString());
			Assert.Equal(8000, input.RootElement.GetProperty("observations")[0].GetProperty("distanceDeltaMeters").GetDouble());
			Assert.Equal(new[] { "seed", "requestedLengthMeters", "outcome", "distanceMeters", "durationSeconds", "ascentMeters", "distanceDeltaMeters", "durationDeltaSeconds" }, input.RootElement.GetProperty("observations")[0].EnumerateObject().Select(x => x.Name));
			var config = root.GetProperty("generationConfig");
			Assert.Equal(1, config.GetProperty("candidateCount").GetInt32());
			Assert.Equal(4096, config.GetProperty("maxOutputTokens").GetInt32());
			Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
			var schema = config.GetProperty("responseJsonSchema");
			Assert.False(schema.TryGetProperty("anyOf", out _));
			Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
			Assert.Equal("nextSearch", Assert.Single(schema.GetProperty("required").EnumerateArray()).GetString());
			var next = schema.GetProperty("properties").GetProperty("nextSearch");
			Assert.Equal(new[] { "object", "null" }, next.GetProperty("type").EnumerateArray().Select(x => x.GetString()));
			Assert.False(next.GetProperty("additionalProperties").GetBoolean());
			Assert.Equal(new[] { "seed", "requestedLengthMeters", "reason" }, next.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
			var properties = next.GetProperty("properties");
			Assert.Equal(3, properties.EnumerateObject().Count());
			Assert.Equal("integer", properties.GetProperty("seed").GetProperty("type").GetString());
			Assert.Equal("number", properties.GetProperty("requestedLengthMeters").GetProperty("type").GetString());
			Assert.Equal(new[] { "distance", "duration", "elevation", "explore" }, properties.GetProperty("reason").GetProperty("enum").EnumerateArray().Select(x => x.GetString()));
			Assert.Equal(10000, properties.GetProperty("requestedLengthMeters").GetProperty("minimum").GetDouble());
			Assert.Equal(30000, properties.GetProperty("requestedLengthMeters").GetProperty("maximum").GetDouble());
			return Response(Envelope(Search));
		}));
		var result = await new GeminiRouteSearchAdvisor(client, Options, TimeProvider.System).AdviseAsync(Context, TestContext.Current.CancellationToken);
		Assert.Equal(new(RouteSearchAction.Search, 7, 16000, RouteSearchReason.Distance), result);
		Assert.Equal(1, calls);
	}

	[Theory]
	[InlineData(1000, 1000, 1500)]
	[InlineData(20000, 10000, 30000)]
	[InlineData(100000, 50000, 100000)]
	public async Task RequestSchemaConstrainsFreshSeedsAndIntersectedLengthBounds(double initial, double minimum, double maximum)
	{
		var context = Context with { InitialLengthMeters = initial, Observations = [Context.Observations[0] with { Seed = 7 }] };
		using var client = new HttpClient(new Handler(async (request, ct) =>
		{
			using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
			var properties = json.RootElement.GetProperty("generationConfig").GetProperty("responseJsonSchema")
				.GetProperty("properties").GetProperty("nextSearch").GetProperty("properties");
			Assert.Equal(Enumerable.Range(3, 14).Where(x => x != 7),
				properties.GetProperty("seed").GetProperty("enum").EnumerateArray().Select(x => x.GetInt32()));
			Assert.Equal(minimum, properties.GetProperty("requestedLengthMeters").GetProperty("minimum").GetDouble());
			Assert.Equal(maximum, properties.GetProperty("requestedLengthMeters").GetProperty("maximum").GetDouble());
			return Response(Envelope(Stop));
		}));
		await new GeminiRouteSearchAdvisor(client, Options, TimeProvider.System).AdviseAsync(context, TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task AcceptsStrictStop() => Assert.Equal(new(RouteSearchAction.Stop, null, null, RouteSearchReason.Stop), await Call(Envelope(Stop)));

	[Theory]
	[InlineData("MAX_TOKENS", "OutputTokenLimit")]
	[InlineData("SAFETY", "IncompleteCandidate")]
	[InlineData("private-provider-text", "IncompleteCandidate")]
	public async Task FailureDiagnosticsAllowlistFinishReasons(string finish, string expected)
	{
		var error = await Assert.ThrowsAsync<RouteSearchAdvisorException>(() => Call(Envelope(Search, finish)));
		Assert.Equal(expected, error.Diagnostic?.ToString());
		Assert.DoesNotContain("private-provider-text", JsonSerializer.Serialize(new { error.Diagnostic, error.HttpStatusCode, error.Failure }));
	}

	[Theory]
	[InlineData("{}", "MalformedEnvelope")]
	[InlineData("{\"promptFeedback\":{\"blockReason\":\"private-provider-text\"}}", "PromptBlocked")]
	public async Task FailureDiagnosticsIdentifyEnvelopeStage(string body, string expected)
	{
		var error = await Assert.ThrowsAsync<RouteSearchAdvisorException>(() => Call(body));
		Assert.Equal(expected, error.Diagnostic?.ToString());
		Assert.DoesNotContain("private-provider-text", error.ToString());
	}

	[Theory]
	[InlineData("{}", "MissingAdviceFields")]
	[InlineData("null", "InvalidAdviceFields")]
	[InlineData("not-json", "InvalidAdviceJson")]
	[InlineData("{\"nextSearch\":{\"seed\":7,\"requestedLengthMeters\":20000,\"reason\":\"private-text\"}}", "InvalidAdviceReason")]
	[InlineData("{\"nextSearch\":{\"seed\":\"private-text\",\"requestedLengthMeters\":20000,\"reason\":\"distance\"}}", "InvalidAdviceSeed")]
	[InlineData("{\"nextSearch\":{\"seed\":7,\"requestedLengthMeters\":\"private-text\",\"reason\":\"distance\"}}", "InvalidAdviceLength")]
	[InlineData("{\"nextSearch\":{\"seed\":7,\"requestedLengthMeters\":999,\"reason\":\"distance\"}}", "SearchLengthInvalid")]
	public async Task FailureDiagnosticsSeparateSchemaFromProposal(string advice, string expected)
	{
		var error = await Assert.ThrowsAsync<RouteSearchAdvisorException>(() => Call(Envelope(advice)));
		Assert.Equal(expected, error.Diagnostic?.ToString());
		Assert.DoesNotContain("private-text", error.ToString());
	}

	[Theory]
	[InlineData("{\"nextSearch\":{\"seed\":2,\"requestedLengthMeters\":10000,\"reason\":\"distance\"}}", "SearchSeedInvalid")]
	[InlineData("{\"nextSearch\":{\"seed\":null,\"requestedLengthMeters\":10000,\"reason\":\"distance\"}}", "InvalidAdviceSeed")]
	public async Task ProposalDiagnosticIdentifiesRejectedRule(string advice, string expected)
	{
		var error = await Assert.ThrowsAsync<RouteSearchAdvisorException>(() => Call(Envelope(advice)));
		Assert.Equal(RouteSearchAdvisorFailure.InvalidResponse, error.Failure);
		Assert.Equal(expected, error.Diagnostic?.ToString());
	}

	[Theory]
	[InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[]}}]}")]
	[InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"thought\":true,\"text\":\"private-text\"}]}}]}")]
	public async Task InvalidPartsHaveSanitizedDiagnostic(string body)
	{
		var error = await Assert.ThrowsAsync<RouteSearchAdvisorException>(() => Call(body));
		Assert.Equal(RouteSearchAdvisorDiagnostic.InvalidParts, error.Diagnostic);
		Assert.DoesNotContain("private-text", error.ToString());
	}

	[Theory]
	[InlineData(false)] [InlineData(true)]
	public async Task TransportErrorsKeepNoProviderMessage(bool io)
	{
		using var client = new HttpClient(new Handler((_, _) => throw (io
			? new IOException("private-text") : new HttpRequestException("private-text"))));
		var error = await Assert.ThrowsAsync<RouteSearchAdvisorException>(() =>
			new GeminiRouteSearchAdvisor(client, Options, TimeProvider.System).AdviseAsync(Context, TestContext.Current.CancellationToken));
		Assert.Equal(RouteSearchAdvisorDiagnostic.TransportError, error.Diagnostic);
		Assert.Null(error.InnerException);
		Assert.DoesNotContain("private-text", error.ToString());
	}

	[Fact]
	public async Task InvalidUtf8HasSanitizedDiagnostic()
	{
		using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{ Content = new ByteArrayContent([0xff]) })));
		var error = await Assert.ThrowsAsync<RouteSearchAdvisorException>(() =>
			new GeminiRouteSearchAdvisor(client, Options, TimeProvider.System).AdviseAsync(Context, TestContext.Current.CancellationToken));
		Assert.Equal(RouteSearchAdvisorDiagnostic.InvalidUtf8, error.Diagnostic);
	}

	public static IEnumerable<object[]> InvalidAdvice()
	{
		foreach (var text in new[] { "{}", "null", "```json\n" + Search + "\n```", Search.Replace("7", "\"7\""),
			Search.Replace("16000", "1e999"), Search.Replace("16000", "999"), Search.Replace("16000", "31000"),
			Search.Replace("7", "2"), Search.Replace("7", "17"), Search.Replace("7", "7.5"), Search.Replace("7", "null"),
			Search.Replace("nextSearch", "NextSearch"), Search.Replace("\"distance\"", "\"stop\""),
			Search.Replace("\"distance\"", "\"safe road\""), Search.Replace("\"reason\":\"distance\"", "\"coordinates\":[]"),
			Search.Replace("\"reason\":\"distance\"", "\"reason\":\"distance\",\"seed\":8"),
			"{\"nextSearch\":null,\"requestedLengthMeters\":20000}", "{\"nextSearch\":null,\"nextSearch\":null}",
			"{\"nextSearch\":{}}", "{\"nextSearch\":[]}", "{\"nextSearch\":\"null\"}",
			Search.Replace("16000", "null"),
			Search.Replace("\"reason\":\"distance\"", "\"reason\":\"distance\",\"coordinates\":[]"),
			"{\"nextSearch\":null,\"action\":\"search\",\"seed\":7,\"requestedLengthMeters\":16000,\"reason\":\"distance\"}",
			"{\"action\":\"search\",\"seed\":7,\"requestedLengthMeters\":16000,\"reason\":\"distance\"}",
			"{\"action\":\"stop\",\"seed\":null,\"requestedLengthMeters\":20000,\"reason\":\"stop\"}",
			"{\"action\":\"stop\",\"seed\":null,\"requestedLengthMeters\":null,\"reason\":\"stop\"}" }) yield return [text];
	}
	[Theory, MemberData(nameof(InvalidAdvice))]
	public async Task RejectsAmbiguousOrUnboundedAdvice(string text) => Assert.Equal(RouteSearchAdvisorFailure.InvalidResponse,
		(await Assert.ThrowsAsync<RouteSearchAdvisorException>(() => Call(Envelope(text)))).Failure);

	[Theory]
	[InlineData("SAFETY")] [InlineData("MAX_TOKENS")] [InlineData("OTHER")]
	public async Task RejectsIncompleteOrBlockedCandidates(string finish) => Assert.Equal(RouteSearchAdvisorFailure.InvalidResponse,
		(await Assert.ThrowsAsync<RouteSearchAdvisorException>(() => Call(Envelope(Search, finish)))).Failure);

	[Theory]
	[InlineData("{}")] [InlineData("null")] [InlineData("{\"candidates\":[{},{}]}")]
	[InlineData("{\"candidates\":[],\"candidates\":[]}")]
	[InlineData("{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}")]
	public async Task RejectsMalformedEnvelope(string body) => Assert.Equal(RouteSearchAdvisorFailure.InvalidResponse,
		(await Assert.ThrowsAsync<RouteSearchAdvisorException>(() => Call(body))).Failure);

	[Theory]
	[InlineData(401, RouteSearchAdvisorFailure.Authentication)] [InlineData(403, RouteSearchAdvisorFailure.Authentication)]
	[InlineData(429, RouteSearchAdvisorFailure.Quota)] [InlineData(500, RouteSearchAdvisorFailure.Unavailable)]
	[InlineData(503, RouteSearchAdvisorFailure.Unavailable)] [InlineData(400, RouteSearchAdvisorFailure.InvalidResponse)]
	[InlineData(302, RouteSearchAdvisorFailure.InvalidResponse)]
	public async Task HttpErrorsAreSanitizedAndNotRetried(int status, RouteSearchAdvisorFailure failure)
	{
		var calls = 0;
		using var client = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Response("private provider detail", status)); }));
		var error = await Assert.ThrowsAsync<RouteSearchAdvisorException>(() => new GeminiRouteSearchAdvisor(client, Options, TimeProvider.System).AdviseAsync(Context, TestContext.Current.CancellationToken));
		Assert.Equal(failure, error.Failure); Assert.Null(error.InnerException); Assert.DoesNotContain("private", error.ToString()); Assert.Equal(1, calls);
		Assert.Equal(RouteSearchAdvisorDiagnostic.HttpError, error.Diagnostic);
		Assert.Equal(status, error.HttpStatusCode);
	}

	[Theory]
	[InlineData("", "test")] [InlineData("key", "../test")] [InlineData("key\r\ninject", "test")]
	public async Task RejectsInvalidConfigurationWithoutNetwork(string key, string model)
	{
		using var client = new HttpClient(new Handler((_, _) => throw new InvalidOperationException()));
		Assert.Equal(RouteSearchAdvisorFailure.NotConfigured, (await Assert.ThrowsAsync<RouteSearchAdvisorException>(() =>
			new GeminiRouteSearchAdvisor(client, new() { ApiKey = key, Model = model }, TimeProvider.System).AdviseAsync(Context, TestContext.Current.CancellationToken))).Failure);
	}

	[Theory]
	[InlineData(262144, false)] [InlineData(262145, false)] [InlineData(262145, true)]
	public async Task ResponseLimitCoversStreamingBodies(int size, bool known)
	{
		var body = Envelope(Search).PadRight(size);
		using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{ Content = known ? new StringContent(body) : new StreamContent(new UnseekableStream(Encoding.UTF8.GetBytes(body))) })));
		var service = new GeminiRouteSearchAdvisor(client, Options, TimeProvider.System);
		if (size == 262144) Assert.Equal(7, (await service.AdviseAsync(Context, TestContext.Current.CancellationToken)).Seed);
		else
		{
			var error = await Assert.ThrowsAsync<RouteSearchAdvisorException>(() => service.AdviseAsync(Context, TestContext.Current.CancellationToken));
			Assert.Equal(RouteSearchAdvisorFailure.InvalidResponse, error.Failure);
			Assert.Equal(RouteSearchAdvisorDiagnostic.ResponseTooLarge, error.Diagnostic);
		}
	}

	[Theory]
	[InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
	public async Task DeadlineCoversHeadersAndBodyAndCallerWins(bool headers, bool cancelCaller)
	{
		using var caller = new CancellationTokenSource();
		var clock = new ManualClock();
		void Fire() { if (cancelCaller) caller.Cancel(); clock.Fire(); }
		using var client = new HttpClient(new Handler(async (_, ct) =>
		{
			if (headers) { Fire(); await Task.Delay(Timeout.Infinite, ct); }
			return new(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream(Fire)) };
		})) { Timeout = Timeout.InfiniteTimeSpan };
		var task = new GeminiRouteSearchAdvisor(client, Options, clock).AdviseAsync(Context, caller.Token);
		if (cancelCaller) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
		else
		{
			var error = await Assert.ThrowsAsync<RouteSearchAdvisorException>(() => task);
			Assert.Equal(RouteSearchAdvisorFailure.Timeout, error.Failure);
			Assert.Equal(RouteSearchAdvisorDiagnostic.Deadline, error.Diagnostic);
		}
	}

	private class UnseekableStream(byte[] bytes) : MemoryStream(bytes) { public override bool CanSeek => false; }
	private sealed class StalledStream(Action fire) : UnseekableStream([])
	{ public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { fire(); await Task.Delay(Timeout.Infinite, cancellationToken); return 0; } }
	private sealed class ManualClock : TimeProvider
	{
		private Action? fire;
		public void Fire() => fire!();
		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{ Assert.Equal(TimeSpan.FromSeconds(30), dueTime); fire = () => callback(state); return new Timer(); }
		private sealed class Timer : ITimer
		{ public bool Change(TimeSpan dueTime, TimeSpan period) => true; public void Dispose() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
	}
}
