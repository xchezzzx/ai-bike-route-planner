using System.Net;
using System.Text.Json;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;
using CyclingRoutes.Infrastructure.Routing;

namespace CyclingRoutes.Tests.Integration;

public class GraphHopperProviderTests
{
	internal const string ValidResponse = """
		{"paths":[{"distance":4567.8,"time":987600,"ascend":15,"descend":7.5,"points_encoded":false,"points":{"type":"LineString","coordinates":[[34.7818,32.0853,12.5],[34.79,32.09,20],[34.82,32.1,22]]},"details":{"surface":[[0,1,"asphalt"],[1,2,"missing"]],"road_class":[[0,1,"secondary"],[1,2,"cycleway"]],"road_environment":[[0,2,"road"]]}}]}
		""";
	private static readonly GeoCoordinate Start = new(32.0853, 34.7818);
	private static readonly GeoCoordinate Destination = new(32.1, 34.82);

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Requests_UseLocalEndpointAndUnencodedLongitudeLatitude(bool loop)
	{
		using var handler = new OpenRouteServiceProviderTests.StubHandler(async (request, ct) =>
		{
			Assert.Equal(HttpMethod.Post, request.Method);
			Assert.Equal("http://127.0.0.1:8989/route", request.RequestUri!.AbsoluteUri);
			Assert.False(request.Headers.Contains("Authorization"));
			using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
			var root = json.RootElement;
			Assert.Equal("road", root.GetProperty("profile").GetString());
			Assert.Equal(loop ? 1 : 2, root.GetProperty("points").GetArrayLength());
			Assert.Equal(34.7818, root.GetProperty("points")[0][0].GetDouble());
			Assert.Equal(32.0853, root.GetProperty("points")[0][1].GetDouble());
			Assert.False(root.GetProperty("points_encoded").GetBoolean());
			Assert.True(root.GetProperty("elevation").GetBoolean());
			Assert.True(root.GetProperty("instructions").GetBoolean());
			Assert.Equal(14000, root.GetProperty("timeout_ms").GetInt32());
			Assert.Equal(new[] { "surface", "road_class", "road_environment" }, root.GetProperty("details").EnumerateArray().Select(x => x.GetString()));
			if (loop)
			{
				Assert.Equal("round_trip", root.GetProperty("algorithm").GetString());
				Assert.Equal(20000, root.GetProperty("round_trip.distance").GetDouble());
				Assert.Equal(3, root.GetProperty("round_trip.seed").GetInt32());
			}
			else
			{
				Assert.Equal(34.82, root.GetProperty("points")[1][0].GetDouble());
				Assert.False(root.TryGetProperty("round_trip.distance", out _));
			}
			return OpenRouteServiceProviderTests.Response(200, ValidResponse);
		});
		using var client = new HttpClient(handler);
		var provider = new GraphHopperProvider(client, new());
		var result = loop ? await provider.GetRoadLoopAsync(Start, 20000, 3, TestContext.Current.CancellationToken)
			: await provider.GetRoadRouteAsync(Start, Destination, TestContext.Current.CancellationToken);
		Assert.Equal(4567.8, result.DistanceMeters);
		Assert.Equal(987.6, result.EstimatedDurationSeconds);
		Assert.Equal(Start, result.Points[0].Position);
		Assert.Equal(Destination, result.Points[^1].Position);
		Assert.Equal(12.5, result.Points[0].ElevationMeters);
		Assert.Equal(15, result.AscentMeters);
		Assert.Contains("GraphHopper", result.Attribution);
		Assert.Contains("OpenStreetMap", result.Attribution);
		Assert.Equal(RouteSurface.Asphalt, result.Evidence!.Segments![0].Surface);
		Assert.Equal(RouteWayType.Road, result.Evidence.Segments[0].WayType);
		Assert.Equal(RouteSurface.Unknown, result.Evidence.Segments[1].Surface);
		Assert.Equal(RouteWayType.Cycleway, result.Evidence.Segments[1].WayType);
		Assert.True(result.Evidence.Surface.UnknownMeters > 0);
	}

	[Fact]
	public async Task MissingDetailsAndElevation_RemainUnknown()
	{
		var body = """{"paths":[{"distance":1000,"time":60000,"points":{"type":"LineString","coordinates":[[34,32],[34.01,32.01]]}}]}""";
		var result = await Route(body);
		Assert.All(result.Points, point => Assert.Null(point.ElevationMeters));
		Assert.Null(result.AscentMeters);
		Assert.Null(result.DescentMeters);
		Assert.False(result.Evidence!.SurfaceSupplied);
		Assert.False(result.Evidence.WaytypeSupplied);
		Assert.Equal(result.Evidence.GeometryLengthMeters, result.Evidence.Surface.UnknownMeters);
		Assert.Equal(result.Evidence.GeometryLengthMeters, result.Evidence.Ways.UnknownMeters);
	}

	[Theory]
	[InlineData("asphalt", RouteSurface.Asphalt)]
	[InlineData("concrete", RouteSurface.Paved)]
	[InlineData("paving_stones", RouteSurface.Paved)]
	[InlineData("cobblestone", RouteSurface.Paved)]
	[InlineData("gravel", RouteSurface.Unpaved)]
	[InlineData("compacted", RouteSurface.Unpaved)]
	[InlineData("ground", RouteSurface.Unpaved)]
	[InlineData("wood", RouteSurface.Other)]
	[InlineData("other", RouteSurface.Other)]
	[InlineData("missing", RouteSurface.Unknown)]
	[InlineData("future_surface", RouteSurface.Unknown)]
	public async Task Surfaces_AreMappedWithoutGuessing(string surface, RouteSurface expected)
	{
		var result = await Route(ValidResponse.Replace("asphalt", surface));
		Assert.Equal(expected, result.Evidence!.Segments![0].Surface);
	}

	[Fact]
	public async Task FerryEnvironment_OverridesRoadClass()
	{
		var result = await Route(ValidResponse.Replace("[0,2,\"road\"]", "[0,1,\"road\"],[1,2,\"ferry\"]"));
		Assert.Equal(RouteWayType.Ferry, result.Evidence!.Segments![1].WayType);
		Assert.True(result.Evidence.Ways.FerryMeters > 0);
	}

	[Fact]
	public async Task PartialDetails_PreserveUnknownGapsAndIntersectPartitions()
	{
		var result = await Route(ValidResponse.Replace("[0,1,\"asphalt\"],[1,2,\"missing\"]", "[1,2,\"asphalt\"]"));
		Assert.Equal(RouteSurface.Unknown, result.Evidence!.Segments![0].Surface);
		Assert.Equal(RouteSurface.Asphalt, result.Evidence.Segments[1].Surface);
		Assert.Equal(result.Evidence.GeometryLengthMeters, result.Evidence.Surface.PavedMeters + result.Evidence.Surface.UnknownMeters, 6);
	}

	[Theory]
	[InlineData("[0,1,\"asphalt\"],[1,2,\"missing\"]", "[0,2,\"asphalt\"],[1,2,\"missing\"]")]
	[InlineData("[0,1,\"asphalt\"]", "[0,3,\"asphalt\"]")]
	[InlineData("[0,1,\"asphalt\"]", "[-1,1,\"asphalt\"]")]
	[InlineData("[0,1,\"asphalt\"]", "[0,0,\"asphalt\"]")]
	[InlineData("[0,1,\"asphalt\"]", "[0,1,4]")]
	[InlineData("\"distance\":4567.8", "\"distance\":0")]
	[InlineData("\"time\":987600", "\"time\":-1")]
	[InlineData("\"ascend\":15", "\"ascend\":-1")]
	[InlineData("34.7818,32.0853,12.5", "34.7818,92,12.5")]
	[InlineData("34.7818,32.0853,12.5", "34.7818,32.0853,1e309")]
	[InlineData("\"points_encoded\":false", "\"points_encoded\":true")]
	[InlineData("\"LineString\"", "\"Polygon\"")]
	public async Task InvalidGeometryMetricsOrIntervals_AreRejected(string oldValue, string newValue)
	{
		var error = await Assert.ThrowsAsync<RoutingException>(() => Route(ValidResponse.Replace(oldValue, newValue)));
		Assert.Equal(RoutingFailure.InvalidResponse, error.Failure);
	}

	[Theory]
	[InlineData(400, "{\"hints\":[{\"details\":\"com.graphhopper.util.exceptions.ConnectionNotFoundException\"}]}", RoutingFailure.NoRoute)]
	[InlineData(400, "{\"hints\":[{\"details\":\"com.graphhopper.util.exceptions.PointNotFoundException\"}]}", RoutingFailure.NoRoute)]
	[InlineData(400, "{\"hints\":[{\"details\":\"com.graphhopper.util.exceptions.MaximumNodesExceededException\"}]}", RoutingFailure.LimitExceeded)]
	[InlineData(400, "{\"message\":\"private location\"}", RoutingFailure.InvalidResponse)]
	[InlineData(401, "private", RoutingFailure.CredentialsRejected)]
	[InlineData(429, "private", RoutingFailure.RateLimited)]
	[InlineData(503, "private", RoutingFailure.Unavailable)]
	[InlineData(302, "private", RoutingFailure.InvalidResponse)]
	[InlineData(200, "{}", RoutingFailure.InvalidResponse)]
	[InlineData(200, "{\"paths\":[]}", RoutingFailure.InvalidResponse)]
	public async Task Errors_AreSanitizedWithNoRetryOrFallback(int status, string body, RoutingFailure expected)
	{
		var calls = 0;
		using var client = new HttpClient(new OpenRouteServiceProviderTests.StubHandler((_, _) =>
		{
			calls++;
			return Task.FromResult(OpenRouteServiceProviderTests.Response(status, body));
		}));
		var error = await Assert.ThrowsAsync<RoutingException>(() => new GraphHopperProvider(client, new())
			.GetRoadRouteAsync(Start, Destination, TestContext.Current.CancellationToken));
		Assert.Equal(expected, error.Failure);
		Assert.DoesNotContain("private", error.ToString());
		Assert.Equal(1, calls);
	}

	[Fact]
	public async Task CallerCancellation_Propagates()
	{
		using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		using var client = new HttpClient(new OpenRouteServiceProviderTests.StubHandler(async (_, ct) =>
		{
			source.Cancel();
			await Task.Delay(Timeout.Infinite, ct);
			return OpenRouteServiceProviderTests.Response(200, ValidResponse);
		}));
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GraphHopperProvider(client, new())
			.GetRoadRouteAsync(Start, Destination, source.Token));
	}

	[Theory]
	[InlineData(true, RoutingFailure.Timeout)]
	[InlineData(false, RoutingFailure.Unavailable)]
	public async Task TransportErrors_AreSanitized(bool timeout, RoutingFailure expected)
	{
		using var client = new HttpClient(new OpenRouteServiceProviderTests.StubHandler((_, _) =>
			throw (timeout ? new TaskCanceledException("private") : new HttpRequestException("private"))));
		var error = await Assert.ThrowsAsync<RoutingException>(() => new GraphHopperProvider(client, new())
			.GetRoadRouteAsync(Start, Destination, TestContext.Current.CancellationToken));
		Assert.Equal(expected, error.Failure);
		Assert.DoesNotContain("private", error.ToString());
	}

	private static async Task<RoutedPath> Route(string body)
	{
		using var client = new HttpClient(new OpenRouteServiceProviderTests.StubHandler((_, _) =>
			Task.FromResult(OpenRouteServiceProviderTests.Response(200, body))));
		return await new GraphHopperProvider(client, new()).GetRoadRouteAsync(Start, Destination, TestContext.Current.CancellationToken);
	}
}
