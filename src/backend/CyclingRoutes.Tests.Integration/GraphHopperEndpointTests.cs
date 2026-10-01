using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace CyclingRoutes.Tests.Integration;

public class GraphHopperEndpointTests
{
	[Fact]
	public async Task DirectRoute_UsesSelectedEngineWithMatchingGpxAndSegments()
	{
		var calls = 0;
		using var factory = Factory(() => { calls++; return GraphHopperProviderTests.ValidResponse; });
		using var client = factory.CreateClient();
		using var response = await client.PostAsJsonAsync("/api/routes/generate", new
		{
			start = new { latitude = 32.0853, longitude = 34.7818 },
			destination = new { latitude = 32.1, longitude = 34.82 },
			shape = "pointToPoint", profile = "road"
		}, TestContext.Current.CancellationToken);
		response.EnsureSuccessStatusCode();
		using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
		var route = json.RootElement;
		Assert.Equal(1, calls);
		Assert.Contains("GraphHopper", route.GetProperty("attribution").GetString());
		Assert.Equal(987.6, route.GetProperty("estimatedDurationSeconds").GetDouble());
		Assert.Equal(3, route.GetProperty("geometry").GetArrayLength());
		Assert.Equal("asphalt", route.GetProperty("segments")[0].GetProperty("surface").GetString());
		Assert.Equal("unknown", route.GetProperty("segments")[1].GetProperty("surface").GetString());
		Assert.Equal("cycleway", route.GetProperty("segments")[1].GetProperty("wayType").GetString());
		XNamespace ns = "http://www.topografix.com/GPX/1/1";
		var points = XDocument.Parse(route.GetProperty("gpx").GetString()!).Descendants(ns + "trkpt").ToArray();
		Assert.Equal(3, points.Length);
		Assert.Equal("34.7818", points[0].Attribute("lon")!.Value);
		Assert.Equal("12.5", points[0].Element(ns + "ele")!.Value);
	}

	[Theory]
	[InlineData(20000, 1)]
	[InlineData(30000, 0)]
	public async Task Loops_PreserveRangeRequirementsAndAttemptBudget(double distance, int retained)
	{
		var calls = 0;
		using var factory = Factory(() =>
		{
			calls++;
			return JsonSerializer.Serialize(new { paths = new[] { new
			{
				distance, time = 3600000, ascend = 20, descend = 20, points_encoded = false,
				points = new { type = "LineString", coordinates = new[]
				{
					new[] { 34.7818, 32.0853, 12.5 }, new[] { 34.79, 32.09, 20 },
					new[] { 34.82, 32.1, 22 }, new[] { 34.7818, 32.0853, 12.5 }
				} }, details = new { surface = new object[][] { [0, 3, "asphalt"] }, road_class = new object[][] { [0, 3, "secondary"] } }
			} } });
		});
		using var client = factory.CreateClient();
		using var response = await client.PostAsJsonAsync("/api/routes/candidates", new
		{
			start = new { latitude = 32.0853, longitude = 34.7818 }, shape = "loop", profile = "road",
			targetDistanceRangeMeters = new { min = 19000, max = 21000 }
		}, TestContext.Current.CancellationToken);
		response.EnsureSuccessStatusCode();
		using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
		Assert.Equal(3, calls);
		Assert.Equal(3, json.RootElement.GetProperty("attemptedCount").GetInt32());
		Assert.Equal(retained, json.RootElement.GetProperty("candidates").GetArrayLength());
		if (retained == 0) Assert.Contains("targets_not_met", json.RootElement.GetProperty("excludedCandidates")[0].GetProperty("reasons").EnumerateArray().Select(x => x.GetString()));
	}

	private static WebApplicationFactory<Program> Factory(Func<string> body) =>
		new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
			.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Access:Mode"] = "Local", ["Routing:Provider"] = "GraphHopper",
				["Routing:GraphHopper:BaseUrl"] = "http://graphhopper.local/",
				["Routing:OpenRouteService:ApiKey"] = "", ["Ai:Gemini:ApiKey"] = ""
			}))
			.ConfigureTestServices(services => services.PostConfigureAll<HttpClientFactoryOptions>(options =>
				options.HttpMessageHandlerBuilderActions.Add(http => http.PrimaryHandler = new OpenRouteServiceProviderTests.StubHandler((request, _) =>
				{
					Assert.Equal("http://graphhopper.local/route", request.RequestUri!.AbsoluteUri);
					return Task.FromResult(OpenRouteServiceProviderTests.Response(200, body()));
				})))));
}
