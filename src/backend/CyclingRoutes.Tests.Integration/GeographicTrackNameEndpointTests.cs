using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;
using CyclingRoutes.Infrastructure.Naming;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CyclingRoutes.Tests.Integration;

public class GeographicTrackNameEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly WebApplicationFactory<Program> _factory;
	public GeographicTrackNameEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

	[Theory]
	[InlineData("generate", false, "Tel-Aviv-Haifa-road-41", false)]
	[InlineData("candidates", true, "Tel-Aviv-loop-road-41", false)]
	[InlineData("plan", true, "Tel-Aviv-loop-road-41", false)]
	[InlineData("generate", false, "Route-road-41", true)]
	[InlineData("candidates", true, "Route-loop-road-41", true)]
	[InlineData("plan", true, "Route-loop-road-41", true)]
	public async Task ActualGeometryNames_AreCanonicalAcrossApiAndGpx(string endpoint, bool loop, string expected, bool missingData)
	{
		var provider = new Provider();
		using var app = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
		{
			services.AddSingleton<IRoutingProvider>(provider);
			services.AddSingleton<IRouteSearchAdvisor>(new NoAdvisor());
			if (missingData) services.AddSingleton<ISettlementLookup>(new GeoNamesSettlementLookup(null));
		}));
		using var client = app.CreateClient(new() { BaseAddress = new("https://localhost") });
		using var response = await client.PostAsJsonAsync($"/api/routes/{endpoint}", new
		{
			// Deliberately different from the provider's snapped geometry.
			start = new { latitude = 31.5, longitude = 35.0 },
			destination = loop ? null : new { latitude = 31.6, longitude = 35.1 },
			shape = loop ? "loop" : "pointToPoint", profile = "road", targetDistanceMeters = 40000
		}, TestContext.Current.CancellationToken);
		response.EnsureSuccessStatusCode();
		using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
		var search = endpoint == "plan" ? json.RootElement.GetProperty("search") : json.RootElement;
		var route = loop ? search.GetProperty("candidates")[0].GetProperty("route") : search;
		Assert.True(route.TryGetProperty("name", out var name), "A generated route must expose the canonical name.");
		Assert.Equal(expected, name.GetString());
		Assert.Matches("^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$", name.GetString()!);
		XNamespace ns = "http://www.topografix.com/GPX/1/1";
		var document = XDocument.Parse(route.GetProperty("gpx").GetString()!);
		Assert.Equal(expected, document.Root!.Element(ns + "trk")!.Element(ns + "name")!.Value);
		var attribution = document.Root.Element(ns + "metadata")!.Element(ns + "desc")!.Value;
		Assert.Contains("ORS & OSM <contributors>", attribution);
		if (missingData) Assert.Equal("ORS & OSM <contributors>", attribution);
		else
		{
			Assert.Contains("GeoNames", attribution);
			Assert.Contains("https://creativecommons.org/licenses/by/4.0/", attribution);
		}
		Assert.Equal(attribution, route.GetProperty("attribution").GetString());
		Assert.Equal(loop ? 4 : 2, document.Descendants(ns + "trkpt").Count());
		Assert.Equal(loop ? (endpoint == "plan" ? 2 : 3) : 1, provider.Calls);
	}

	private sealed class NoAdvisor : IRouteSearchAdvisor
	{
		public Task<RouteSearchAdvice> AdviseAsync(RouteSearchContext context, CancellationToken cancellationToken) =>
			throw new InvalidOperationException("Naming must not call an LLM.");
	}

	private sealed class Provider : IRoutingProvider
	{
		public int Calls { get; private set; }
		private static readonly RoutePoint TelAviv = new(new(32.08088, 34.78057), 10);
		public Task<RoutedPath> GetRoadRouteAsync(GeoCoordinate start, GeoCoordinate destination, CancellationToken cancellationToken)
		{
			Calls++;
			return Task.FromResult(Path([TelAviv, new(new(32.81841, 34.9885), 20)]));
		}
		public Task<RoutedPath> GetRoadLoopAsync(GeoCoordinate start, double requestedLengthMeters, int seed, CancellationToken cancellationToken)
		{
			Calls++;
			return Task.FromResult(Path([TelAviv, new(new(32.1, 34.8), 20), new(new(32.07, 34.82), 20), TelAviv]));
		}
		private static RoutedPath Path(IReadOnlyList<RoutePoint> points) => new(points, 40500, 7200, 100, 100, "ORS & OSM <contributors>");
	}
}
