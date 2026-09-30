using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Contracts.RoutePlanning;
using CyclingRoutes.Infrastructure.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CyclingRoutes.Tests.Integration;

public class RouteCandidatesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly WebApplicationFactory<Program> _factory;
	public RouteCandidatesEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

	[Fact]
	public async Task Candidates_ReturnRankedRoutesWithMatchingGpx()
	{
		var calls = 0;
		using var factory = WithProvider((_, _) =>
		{
			var seed = ++calls;
			return Task.FromResult(OpenRouteServiceProviderTests.Response(200, LoopResponse(seed, seed == 1 ? 21000 : 20000)));
		});
		using var client = Client(factory);
		using var response = await client.PostAsJsonAsync("/api/routes/candidates", Request(), TestContext.Current.CancellationToken);
		Assert.Equal(200, (int)response.StatusCode);
		var result = (await response.Content.ReadFromJsonAsync<RouteCandidatesResponse>(TestContext.Current.CancellationToken))!;
		Assert.Equal(new[] { 2, 3, 1 }, result.Candidates.Select(x => x.Seed));
		Assert.Equal(3, calls);
		Assert.Equal(3, result.AttemptedCount);
		Assert.Equal(20000, result.RequestedLengthMeters);
		Assert.Empty(result.Assumptions);
		Assert.Equal(new[] { "candidate_search_limited" }, result.Warnings);
		Assert.Equal(1000, result.Candidates[2].Assessment.DistanceDeltaMeters);
		Assert.Equal(0.05, result.Candidates[2].Assessment.Score);
		foreach (var candidate in result.Candidates)
		{
			Assert.Null(candidate.Assessment.DurationDeltaSeconds);
			Assert.True(candidate.Assessment.TargetsMatched);
			Assert.Contains("road_surface_unknown", candidate.Route.Warnings);
			Assert.Equal("unavailable", candidate.Assessment.Quality!.SurfaceEvidenceState);
			Assert.Contains("OpenStreetMap", candidate.Route.Attribution);
			AssertGpx(candidate.Route);
		}
		using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
		Assert.False(json.RootElement.TryGetProperty("incompleteFailure", out _));
	}

	[Theory]
	[InlineData("quota", "routing_rate_limited")]
	[InlineData("invalid-loop", "routing_invalid_response")]
	[InlineData("timeout", "routing_timeout")]
	public async Task PartialFailure_ReturnsSafeWarningAndOriginalGpx(string scenario, string code)
	{
		var calls = 0;
		using var factory = WithProvider((_, _) =>
		{
			if (++calls == 1) return Task.FromResult(OpenRouteServiceProviderTests.Response(200, LoopResponse(1)));
			if (scenario == "timeout") throw new TaskCanceledException();
			return Task.FromResult(scenario == "quota" ? OpenRouteServiceProviderTests.Response(429, "private secret provider detail")
				: OpenRouteServiceProviderTests.Response(200, OpenRouteServiceProviderTests.ValidResponse));
		});
		using var client = Client(factory);
		using var response = await client.PostAsJsonAsync("/api/routes/candidates", Request(), TestContext.Current.CancellationToken);
		Assert.Equal(200, (int)response.StatusCode);
		var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		Assert.DoesNotContain("private", body);
		Assert.DoesNotContain("test-key", body);
		var result = JsonSerializer.Deserialize<RouteCandidatesResponse>(body, JsonSerializerOptions.Web)!;
		Assert.Contains("candidate_generation_incomplete", result.Warnings);
		Assert.Contains(code, result.Warnings);
		Assert.Equal(2, calls);
		Assert.Equal(2, result.AttemptedCount);
		var candidate = Assert.Single(result.Candidates);
		Assert.Equal(1, candidate.Seed);
		AssertGpx(candidate.Route);
	}

	[Theory]
	[InlineData("duration-only")]
	[InlineData("outside-tolerance")]
	public async Task ApproximateSearch_ReturnsExplicitAssumptionsAndWarnings(string scenario)
	{
		using var factory = WithProvider((_, _) => Task.FromResult(OpenRouteServiceProviderTests.Response(200, LoopResponse(1, 30000))));
		using var client = Client(factory);
		var request = Request();
		if (scenario == "duration-only") { request.Remove("targetDistanceMeters"); request["targetDurationSeconds"] = 3600; }
		using var response = await client.PostAsJsonAsync("/api/routes/candidates", request, TestContext.Current.CancellationToken);
		Assert.Equal(200, (int)response.StatusCode);
		var result = (await response.Content.ReadFromJsonAsync<RouteCandidatesResponse>(TestContext.Current.CancellationToken))!;
		Assert.Equal(3, result.AttemptedCount);
		if (scenario == "duration-only")
		{
			var candidate = Assert.Single(result.Candidates);
			Assert.Contains("initial_speed_20_kmh", result.Assumptions);
			Assert.Null(candidate.Assessment.DistanceDeltaMeters);
			Assert.Equal(0, candidate.Assessment.DurationDeltaSeconds);
			Assert.True(candidate.Assessment.TargetsMatched);
		}
		else
		{
			Assert.Contains("no_candidate_within_tolerance", result.Warnings);
			Assert.Empty(result.Candidates);
			var excluded = Assert.Single(result.ExcludedCandidates);
			Assert.Contains("targets_not_met", excluded.Reasons);
			Assert.False(excluded.Assessment.TargetsMatched);
		}
	}

	[Theory]
	[InlineData("Development")]
	[InlineData("Production")]
	public async Task InvalidRequests_Return400BeforeProvider(string environment)
	{
		var calls = 0;
		using var factory = WithProvider((_, _) => { calls++; throw new InvalidOperationException("must not call provider"); }, environment);
		using var client = Client(factory);
		foreach (var body in new[] { "{", "{}", "{\"start\":\"wrong\"}",
			"{\"start\":{\"latitude\":32,\"longitude\":34},\"shape\":\"loop\",\"profile\":\"road\"}" })
		{
			using var content = new StringContent(body, Encoding.UTF8, "application/json");
			using var response = await client.PostAsync("/api/routes/candidates", content, TestContext.Current.CancellationToken);
			Assert.Equal(400, (int)response.StatusCode);
		}
		Assert.Equal(0, calls);
	}

	[Theory]
	[InlineData("Development", "unknown")]
	[InlineData("Production", "unknown")]
	[InlineData("Development", "targetDistanceMeters")]
	[InlineData("Production", "targetDistanceMeters")]
	public async Task StrictJson_RejectsOtherwiseValidRequest(string environment, string field)
	{
		var calls = 0;
		using var factory = WithProvider((_, _) =>
		{
			calls++;
			return Task.FromResult(OpenRouteServiceProviderTests.Response(200, LoopResponse(1)));
		}, environment);
		using var client = Client(factory);
		var request = Request();
		request[field] = field == "unknown" ? 1 : "20000";
		using var response = await client.PostAsJsonAsync("/api/routes/candidates", request, TestContext.Current.CancellationToken);
		Assert.Equal(400, (int)response.StatusCode);
		Assert.Equal(0, calls);
	}

	[Fact]
	public async Task DecimalDistanceBoundary_DoesNotProduceTargetWarnings()
	{
		using var factory = WithProvider((_, _) => Task.FromResult(OpenRouteServiceProviderTests.Response(200, LoopResponse(1, 11002.2))));
		using var client = Client(factory);
		var request = Request();
		request["targetDistanceMeters"] = 10002;
		using var response = await client.PostAsJsonAsync("/api/routes/candidates", request, TestContext.Current.CancellationToken);
		Assert.Equal(200, (int)response.StatusCode);
		var result = (await response.Content.ReadFromJsonAsync<RouteCandidatesResponse>(TestContext.Current.CancellationToken))!;
		var candidate = Assert.Single(result.Candidates);
		Assert.True(candidate.Assessment.TargetsMatched);
		Assert.DoesNotContain("targets_not_met", candidate.Route.Warnings);
		Assert.DoesNotContain("no_candidate_within_tolerance", result.Warnings);
	}

	[Theory]
	[InlineData("gravel", 20000, "unsupported_intent")]
	[InlineData("road", 999, "search_distance_out_of_range")]
	[InlineData("road", 100001, "search_distance_out_of_range")]
	public async Task UnsupportedSearch_Returns422WithoutProvider(string profile, double distance, string code)
	{
		using var factory = WithProvider((_, _) => throw new InvalidOperationException("must not call provider"));
		using var client = Client(factory);
		var request = Request(); request["profile"] = profile; request["targetDistanceMeters"] = distance;
		using var response = await client.PostAsJsonAsync("/api/routes/candidates", request, TestContext.Current.CancellationToken);
		await AssertProblem(response, 422, code);
	}

	[Theory]
	[InlineData("no-route", 422, "route_not_found", 3)]
	[InlineData("invalid-loop", 502, "routing_invalid_response", 1)]
	[InlineData("timeout", 504, "routing_timeout", 1)]
	[InlineData("limit", 422, "routing_limit_exceeded", 1)]
	[InlineData("quota", 503, "routing_rate_limited", 1)]
	public async Task FirstFailures_ReturnProblemNeverEmptySuccess(string scenario, int status, string code, int expectedCalls)
	{
		var calls = 0;
		using var factory = WithProvider((_, _) =>
		{
			calls++;
			if (scenario == "timeout") throw new TaskCanceledException();
			return Task.FromResult(scenario switch
			{
				"no-route" => OpenRouteServiceProviderTests.Response(404, "{\"error\":{\"code\":2009}}"),
				"limit" => OpenRouteServiceProviderTests.Response(400, "{\"error\":{\"code\":2004}}"),
				"quota" => OpenRouteServiceProviderTests.Response(429, "private detail"),
				_ => OpenRouteServiceProviderTests.Response(200, OpenRouteServiceProviderTests.ValidResponse)
			});
		});
		using var client = Client(factory);
		using var response = await client.PostAsJsonAsync("/api/routes/candidates", Request(), TestContext.Current.CancellationToken);
		await AssertProblem(response, status, code);
		Assert.Equal(expectedCalls, calls);
	}

	[Fact]
	public async Task MissingKey_Returns503()
	{
		using var factory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production").UseSetting("Access:Mode", "Local")
			.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
				new Dictionary<string, string?> { ["Routing:OpenRouteService:ApiKey"] = "" }))
			.ConfigureTestServices(OfflineProviders.Configure));
		using var client = Client(factory);
		Assert.True(string.IsNullOrEmpty(factory.Services.GetRequiredService<OpenRouteServiceOptions>().ApiKey), "Routing credentials must be disabled.");
		using var response = await client.PostAsJsonAsync("/api/routes/candidates", Request(), TestContext.Current.CancellationToken);
		await AssertProblem(response, 503, "routing_not_configured");
	}

	private WebApplicationFactory<Program> WithProvider(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, string environment = "Production") =>
		_factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment).UseSetting("Access:Mode", "Local").ConfigureTestServices(services =>
		{
			services.AddSingleton(new OpenRouteServiceOptions { ApiKey = "test-key" });
			services.AddHttpClient<IRoutingProvider, OpenRouteServiceProvider>()
				.ConfigurePrimaryHttpMessageHandler(() => new OpenRouteServiceProviderTests.StubHandler(send));
		}));
	private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new() { BaseAddress = new("https://localhost") });
	private static Dictionary<string, object> Request() => new()
	{
		["start"] = new { latitude = 32.0853, longitude = 34.7818 },
		["shape"] = "loop", ["profile"] = "road", ["targetDistanceMeters"] = 20000
	};
	private static string LoopResponse(int seed, double distance = 20000) => JsonSerializer.Serialize(new
	{
		type = "FeatureCollection", features = new[] { new
		{
			type = "Feature", geometry = new { type = "LineString", coordinates = new[]
			{
				new[] { 34.7818, 32.0853, 1 }, new[] { 34.79, 32.09 + seed * 0.001, 2 },
				new[] { 34.8, 32.08, 3 }, new[] { 34.7818, 32.0853, 1 }
			} }, properties = new { summary = new { distance, duration = 3600 }, ascent = 100, descent = 100 }
		} }
	});
	private static void AssertGpx(GeneratedRouteResponse route)
	{
		XNamespace ns = "http://www.topografix.com/GPX/1/1";
		var points = XDocument.Parse(route.Gpx).Descendants(ns + "trkpt").ToArray();
		Assert.Equal(route.Geometry.Count, points.Length);
		for (var i = 0; i < points.Length; i++)
		{
			Assert.Equal(route.Geometry[i].Latitude, double.Parse(points[i].Attribute("lat")!.Value, CultureInfo.InvariantCulture));
			Assert.Equal(route.Geometry[i].Longitude, double.Parse(points[i].Attribute("lon")!.Value, CultureInfo.InvariantCulture));
			Assert.Equal(route.Geometry[i].ElevationMeters, double.Parse(points[i].Element(ns + "ele")!.Value, CultureInfo.InvariantCulture));
		}
	}
	private static async Task AssertProblem(HttpResponseMessage response, int status, string code)
	{
		Assert.Equal(status, (int)response.StatusCode);
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
		var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		Assert.DoesNotContain("private", text);
		using var json = JsonDocument.Parse(text);
		Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
		Assert.False(json.RootElement.TryGetProperty("candidates", out _));
	}
}
