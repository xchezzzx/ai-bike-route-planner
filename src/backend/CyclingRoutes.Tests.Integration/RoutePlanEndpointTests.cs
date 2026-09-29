using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CyclingRoutes.Tests.Integration;

public class RoutePlanEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly WebApplicationFactory<Program> factory;
	public RoutePlanEndpointTests(WebApplicationFactory<Program> factory) => this.factory = factory;
	private const string Body = """{"start":{"latitude":32,"longitude":34},"shape":"loop","profile":"road","targetDistanceMeters":20000}""";
	private WebApplicationFactory<Program> With(Provider provider, Advisor advisor) => factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
	{ s.AddSingleton<IRoutingProvider>(provider); s.AddSingleton<IRouteSearchAdvisor>(advisor); }));
	private static HttpClient Client(WebApplicationFactory<Program> app) => app.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });

	[Theory]
	[InlineData("search", "searched", 3, 1)] [InlineData("stop", "stopped", 2, 1)]
	[InlineData("quota", "failed", 3, 1)] [InlineData("matched", "notNeeded", 2, 0)]
	[InlineData("noInitial", "skippedNoCandidates", 3, 0)] [InlineData("partial", "skippedRoutingFailure", 2, 0)]
	public async Task PlansReturnBoundedTraceAndOriginalGpx(string scenario, string status, int routeCalls, int adviceCalls)
	{
		var provider = new Provider((seed, _) =>
		{
			if (scenario == "partial" && seed == 2) throw new RoutingException(RoutingFailure.RateLimited);
			if (scenario == "noInitial" && seed < 3) throw new RoutingException(RoutingFailure.NoRoute);
			return Task.FromResult(Path(seed, scenario == "matched" ? 20000 : 28000));
		});
		var advisor = new Advisor((_, _) => scenario switch
		{
			"quota" => throw new RouteSearchAdvisorException(RouteSearchAdvisorFailure.Quota),
			"stop" => Task.FromResult(new RouteSearchAdvice(RouteSearchAction.Stop, null, null, RouteSearchReason.Stop)),
			_ => Task.FromResult(new RouteSearchAdvice(RouteSearchAction.Search, 7, 16000, RouteSearchReason.Distance))
		});
		using var app = With(provider, advisor);
		using var client = Client(app);
		using var response = await client.PostAsync("/api/routes/plan", new StringContent(Body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		using var json = JsonDocument.Parse(text);
		var root = json.RootElement;
		Assert.Equal(status, root.GetProperty("advisorStatus").GetString());
		Assert.Equal(adviceCalls, root.GetProperty("advisorCallCount").GetInt32());
		Assert.Equal(routeCalls, root.GetProperty("search").GetProperty("attemptedCount").GetInt32());
		Assert.Equal(routeCalls, root.GetProperty("attempts").GetArrayLength());
		Assert.Equal(routeCalls, provider.Calls); Assert.Equal(adviceCalls, advisor.Calls);
		Assert.Equal(scenario == "quota" ? "quota" : null, root.GetProperty("advisorFailure").GetString());
		if (scenario == "partial") Assert.Contains("routing_rate_limited", text);
		foreach (var candidate in root.GetProperty("search").GetProperty("candidates").EnumerateArray())
		{
			var route = candidate.GetProperty("route");
			var geometry = route.GetProperty("geometry");
			XNamespace ns = "http://www.topografix.com/GPX/1/1";
			Assert.Equal(geometry.GetArrayLength(), XDocument.Parse(route.GetProperty("gpx").GetString()!).Descendants(ns + "trkpt").Count());
			Assert.Equal(GpxWriter.Write(Path(candidate.GetProperty("seed").GetInt32(), scenario == "matched" ? 20000 : 28000)), route.GetProperty("gpx").GetString());
		}
	}

	[Theory]
	[InlineData("{", 400)] [InlineData("null", 400)] [InlineData("{}", 400)]
	[InlineData("\"wrong\"", 400)]
	public async Task MalformedRequestDoesNotCallProviders(string body, int status) => await Rejected(body, "application/json", status);

	[Theory]
	[InlineData("text/plain", 415)] [InlineData("application/json; charset=utf-16", 415)]
	public async Task UnsupportedMediaDoesNotCallProviders(string type, int status) => await Rejected(Body, type, status);

	[Theory]
	[InlineData(false)] [InlineData(true)]
	public async Task OversizeKnownAndChunkedBodiesRejected(bool chunked)
	{
		var provider = new Provider((_, _) => throw new InvalidOperationException());
		using var app = With(provider, new Advisor((_, _) => throw new InvalidOperationException()));
		using var client = Client(app);
		var bytes = Encoding.UTF8.GetBytes(Body.PadRight(65537));
		using HttpContent content = chunked ? new StreamContent(new UnseekableStream(bytes)) : new ByteArrayContent(bytes);
		content.Headers.ContentType = new("application/json");
		using var response = await client.PostAsync("/api/routes/plan", content, TestContext.Current.CancellationToken);
		Assert.Equal(413, (int)response.StatusCode); Assert.Equal(0, provider.Calls);
	}

	[Theory]
	[InlineData("\"targetDistanceMeters\":20000", "\"targetDistanceMeters\":\"20000\"", 400)]
	[InlineData("\"targetDistanceMeters\":20000", "\"targetDistanceMeters\":-1", 400)]
	[InlineData("\"targetDistanceMeters\":20000", "\"targetDistanceMeters\":20000,\"unknown\":1", 400)]
	[InlineData("\"targetDistanceMeters\":20000", "\"targetDistanceMeters\":20000,\"targetDistanceMeters\":30000", 400)]
	[InlineData("\"targetDistanceMeters\":20000", "\"targetDistanceMeters\":20000,\"TargetDistanceMeters\":30000", 400)]
	[InlineData("\"latitude\":32", "\"latitude\":32,\"latitude\":33", 400)]
	[InlineData("\"latitude\":32", "\"latitude\":32,\"Latitude\":33", 400)]
	[InlineData("\"road\"", "\"gravel\"", 422)]
	[InlineData("\"targetDistanceMeters\":20000", "\"targetDistanceMeters\":100001", 422)]
	public async Task StrictValidationRunsBeforeProviders(string oldValue, string newValue, int status) => await Rejected(Body.Replace(oldValue, newValue), "application/json", status);

	[Fact]
	public async Task CancellationReachesRoutingProvider()
	{
		using var caller = new CancellationTokenSource();
		var provider = new Provider(async (_, ct) => { caller.Cancel(); await Task.Delay(Timeout.Infinite, ct); return Path(1); });
		using var app = With(provider, new Advisor((_, _) => throw new InvalidOperationException()));
		using var client = Client(app);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PostAsync("/api/routes/plan", new StringContent(Body, Encoding.UTF8, "application/json"), caller.Token));
		Assert.Equal(1, provider.Calls);
	}

	[Fact]
	public async Task NoUsableRoutesReturnsExistingProblemCode()
	{
		using var app = With(new Provider((_, _) => throw new RoutingException(RoutingFailure.NoRoute)), new Advisor((_, _) => throw new InvalidOperationException()));
		using var client = Client(app);
		using var response = await client.PostAsync("/api/routes/plan", new StringContent(Body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
		Assert.Equal(422, (int)response.StatusCode);
		Assert.Contains("route_not_found", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
	}

	private async Task Rejected(string body, string media, int status)
	{
		var provider = new Provider((_, _) => throw new InvalidOperationException());
		var advisor = new Advisor((_, _) => throw new InvalidOperationException());
		using var app = With(provider, advisor);
		using var client = Client(app);
		using var content = new StringContent(body);
		content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(media);
		using var response = await client.PostAsync("/api/routes/plan", content, TestContext.Current.CancellationToken);
		Assert.Equal(status, (int)response.StatusCode); Assert.Equal(0, provider.Calls); Assert.Equal(0, advisor.Calls);
	}
	private static RoutedPath Path(int seed, double distance = 28000) => new(
		[new(new(32, 34), 1), new(new(32 + seed * 0.001, 34.01), 2), new(new(32.01, 34.02), 3), new(new(32, 34), 1)], distance, 3600, 100, 100, "test");
	private sealed class Provider(Func<int, CancellationToken, Task<RoutedPath>> run) : IRoutingProvider
	{
		public int Calls { get; private set; }
		public Task<RoutedPath> GetRoadLoopAsync(GeoCoordinate start, double requestedLengthMeters, int seed, CancellationToken cancellationToken) { Calls++; return run(seed, cancellationToken); }
		public Task<RoutedPath> GetRoadRouteAsync(GeoCoordinate start, GeoCoordinate destination, CancellationToken cancellationToken) => throw new InvalidOperationException();
	}
	private sealed class Advisor(Func<RouteSearchContext, CancellationToken, Task<RouteSearchAdvice>> run) : IRouteSearchAdvisor
	{
		public int Calls { get; private set; }
		public Task<RouteSearchAdvice> AdviseAsync(RouteSearchContext context, CancellationToken cancellationToken) { Calls++; return run(context, cancellationToken); }
	}
	private sealed class UnseekableStream(byte[] bytes) : MemoryStream(bytes) { public override bool CanSeek => false; }
}
