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
	public const string Search = """{"action":"search","seed":7,"requestedLengthMeters":16000,"reason":"distance"}""";
	public const string Stop = """{"action":"stop","seed":null,"requestedLengthMeters":null,"reason":"stop"}""";
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
			Assert.Contains("route-search-v1", root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
			using var input = JsonDocument.Parse(root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!);
			Assert.Equal(new[] { "preferences", "initialLengthMeters", "observations" }, input.RootElement.EnumerateObject().Select(x => x.Name));
			Assert.Equal(new[] { "shape", "profile", "elevation", "targetDistanceMeters", "targetDurationSeconds" }, input.RootElement.GetProperty("preferences").EnumerateObject().Select(x => x.Name));
			Assert.Equal("road", input.RootElement.GetProperty("preferences").GetProperty("profile").GetString());
			Assert.Equal(8000, input.RootElement.GetProperty("observations")[0].GetProperty("distanceDeltaMeters").GetDouble());
			Assert.Equal(new[] { "seed", "requestedLengthMeters", "outcome", "distanceMeters", "durationSeconds", "ascentMeters", "distanceDeltaMeters", "durationDeltaSeconds" }, input.RootElement.GetProperty("observations")[0].EnumerateObject().Select(x => x.Name));
			var config = root.GetProperty("generationConfig");
			Assert.Equal(1, config.GetProperty("candidateCount").GetInt32());
			Assert.Equal(1024, config.GetProperty("maxOutputTokens").GetInt32());
			Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
			Assert.False(config.GetProperty("responseJsonSchema").GetProperty("additionalProperties").GetBoolean());
			Assert.Equal(4, config.GetProperty("responseJsonSchema").GetProperty("required").GetArrayLength());
			return Response(Envelope(Search));
		}));
		var result = await new GeminiRouteSearchAdvisor(client, Options, TimeProvider.System).AdviseAsync(Context, TestContext.Current.CancellationToken);
		Assert.Equal(new(RouteSearchAction.Search, 7, 16000, RouteSearchReason.Distance), result);
		Assert.Equal(1, calls);
	}

	[Fact]
	public async Task AcceptsStrictStop() => Assert.Equal(new(RouteSearchAction.Stop, null, null, RouteSearchReason.Stop), await Call(Envelope(Stop)));

	public static IEnumerable<object[]> InvalidAdvice()
	{
		foreach (var text in new[] { "{}", "null", "```json\n" + Search + "\n```", Search.Replace("7", "\"7\""),
			Search.Replace("16000", "1e999"), Search.Replace("16000", "999"), Search.Replace("16000", "31000"),
			Search.Replace("7", "2"), Search.Replace("7", "17"), Search.Replace("7", "7.5"), Search.Replace("7", "null"),
			Search.Replace("\"search\"", "\"Search\""), Search.Replace("\"distance\"", "\"stop\""),
			Search.Replace("\"distance\"", "\"safe road\""), Search.Replace("\"reason\":\"distance\"", "\"coordinates\":[]"),
			Search.Replace("\"reason\":\"distance\"", "\"reason\":\"distance\",\"seed\":8"),
			Stop.Replace("\"seed\":null", "\"seed\":3"), Stop.Replace("\"requestedLengthMeters\":null", "\"requestedLengthMeters\":20000"),
			Stop.Replace("\"reason\":\"stop\"", "\"reason\":\"explore\"") }) yield return [text];
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
		else Assert.Equal(RouteSearchAdvisorFailure.InvalidResponse, (await Assert.ThrowsAsync<RouteSearchAdvisorException>(() => service.AdviseAsync(Context, TestContext.Current.CancellationToken))).Failure);
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
		else Assert.Equal(RouteSearchAdvisorFailure.Timeout, (await Assert.ThrowsAsync<RouteSearchAdvisorException>(() => task)).Failure);
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
