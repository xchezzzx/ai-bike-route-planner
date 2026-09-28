using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Infrastructure.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CyclingRoutes.Tests.Integration;

public class RouteGenerationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly WebApplicationFactory<Program> _factory;
	public RouteGenerationEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

	[Fact]
	public async Task SuccessfulGeneration_ReturnsProviderGeometryAndMatchingGpx()
	{
		using var factory = WithProviderResponse(200, OpenRouteServiceProviderTests.ValidResponse);
		using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
		using var content = ValidContent();
		using var response = await client.PostAsync("/api/routes/generate", content, TestContext.Current.CancellationToken);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
		Assert.Equal(4567.8, json.RootElement.GetProperty("distanceMeters").GetDouble());
		Assert.Equal(987.6, json.RootElement.GetProperty("estimatedDurationSeconds").GetDouble());
		Assert.Equal(2, json.RootElement.GetProperty("geometry").GetArrayLength());
		Assert.Equal(32.0853, json.RootElement.GetProperty("geometry")[0].GetProperty("latitude").GetDouble());
		Assert.Equal("targets_not_optimized", json.RootElement.GetProperty("warnings")[0].GetString());
		XNamespace ns = "http://www.topografix.com/GPX/1/1";
		var gpx = XDocument.Parse(json.RootElement.GetProperty("gpx").GetString()!);
		var points = gpx.Descendants(ns + "trkpt").ToArray();
		Assert.Equal(2, points.Length);
		Assert.Equal("34.7818", points[0].Attribute("lon")!.Value);
		Assert.Equal("32.1", points[1].Attribute("lat")!.Value);
	}

	[Theory]
	[InlineData(401, "do not expose this", 503, "routing_credentials_rejected")]
	[InlineData(429, "do not expose this", 503, "routing_rate_limited")]
	[InlineData(503, "do not expose this", 503, "routing_unavailable")]
	[InlineData(200, "{}", 502, "routing_invalid_response")]
	[InlineData(404, "{\"error\":{\"code\":2009}}", 422, "route_not_found")]
	[InlineData(400, "{\"error\":{\"code\":2004}}", 422, "routing_limit_exceeded")]
	public async Task UpstreamFailures_ReturnPublicCodes(int upstreamStatus, string upstreamBody, int status, string code)
	{
		using var factory = WithProviderResponse(upstreamStatus, upstreamBody);
		using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
		using var content = ValidContent();
		using var response = await client.PostAsync("/api/routes/generate", content, TestContext.Current.CancellationToken);
		Assert.Equal(status, (int)response.StatusCode);
		var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		Assert.DoesNotContain("do not expose this", body);
		using var json = JsonDocument.Parse(body);
		Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
	}

	private WebApplicationFactory<Program> WithProviderResponse(int status, string body) =>
		_factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
		{
			services.AddSingleton(new OpenRouteServiceOptions { ApiKey = "test-key" });
			services.AddHttpClient<IRoutingProvider, OpenRouteServiceProvider>()
				.ConfigurePrimaryHttpMessageHandler(() => new OpenRouteServiceProviderTests.StubHandler(
					(_, _) => Task.FromResult(OpenRouteServiceProviderTests.Response(status, body))));
		}));

	[Fact]
	public async Task ProviderTimeout_Returns504()
	{
		using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
		{
			services.AddSingleton(new OpenRouteServiceOptions { ApiKey = "test-key" });
			services.AddHttpClient<IRoutingProvider, OpenRouteServiceProvider>()
				.ConfigurePrimaryHttpMessageHandler(() => new OpenRouteServiceProviderTests.StubHandler((_, _) => throw new TaskCanceledException()));
		}));
		using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost") });
		using var content = ValidContent();
		using var response = await client.PostAsync("/api/routes/generate", content, TestContext.Current.CancellationToken);
		Assert.Equal(504, (int)response.StatusCode);
		using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
		Assert.Equal("routing_timeout", json.RootElement.GetProperty("code").GetString());
	}

	private static StringContent ValidContent() => new("""
		{"start":{"latitude":32.0853,"longitude":34.7818},"destination":{"latitude":32.1,"longitude":34.82},"shape":"pointToPoint","profile":"road","targetDistanceMeters":10000}
		""", Encoding.UTF8, "application/json");

	[Theory]
	[InlineData("pointToPoint", "road", "balanced", 503, "routing_not_configured")]
	[InlineData("pointToPoint", "gravel", "balanced", 422, "unsupported_intent")]
	[InlineData("pointToPoint", "road", "seekClimbs", 422, "unsupported_intent")]
	[InlineData("pointToPoint", "road", "minimize", 422, "unsupported_intent")]
	[InlineData("loop", "road", "balanced", 422, "unsupported_intent")]
	public async Task UnavailableGeneration_ReturnsHonestProblem(string shape, string profile, string elevation, int status, string code)
	{
		using var factory = _factory.WithWebHostBuilder(builder => builder
			.UseEnvironment("Production")
			.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
				new Dictionary<string, string?> { ["Routing:OpenRouteService:ApiKey"] = "" })));
		using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
		var json = JsonSerializer.Serialize(new
		{
			start = new { latitude = 32.0853, longitude = 34.7818 },
			destination = shape == "loop" ? null : new { latitude = 32.1, longitude = 34.82 },
			shape, profile, elevation, targetDistanceMeters = 10000
		});
		using var content = new StringContent(json, Encoding.UTF8, "application/json");
		using var response = await client.PostAsync("/api/routes/generate", content, TestContext.Current.CancellationToken);
		Assert.Equal(status, (int)response.StatusCode);
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
		using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
		Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
		Assert.False(body.RootElement.TryGetProperty("gpx", out _));
	}

	[Fact]
	public async Task InvalidRequest_ReturnsFieldErrorsBeforeRouting()
	{
		using var client = _factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
		using var content = new StringContent("{}", Encoding.UTF8, "application/json");
		using var response = await client.PostAsync("/api/routes/generate", content, TestContext.Current.CancellationToken);
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
		Assert.Equal("required", body.RootElement.GetProperty("errors").GetProperty("start")[0].GetString());
	}
}
