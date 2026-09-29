using System.Text.Json;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Infrastructure.Routing;

namespace CyclingRoutes.Tests.Integration;

public class OpenRouteServiceEvidenceTests
{
	[Theory]
	[InlineData(0, "UnknownMeters")]
	[InlineData(1, "PavedMeters")]
	[InlineData(2, "NonRoadMeters")]
	[InlineData(3, "PavedMeters")]
	[InlineData(4, "PavedMeters")]
	[InlineData(5, "OtherKnownMeters")]
	[InlineData(6, "OtherKnownMeters")]
	[InlineData(7, "OtherKnownMeters")]
	[InlineData(8, "NonRoadMeters")]
	[InlineData(9, "NonRoadMeters")]
	[InlineData(10, "NonRoadMeters")]
	[InlineData(11, "NonRoadMeters")]
	[InlineData(12, "NonRoadMeters")]
	[InlineData(13, "NonRoadMeters")]
	[InlineData(14, "OtherKnownMeters")]
	[InlineData(15, "NonRoadMeters")]
	[InlineData(16, "NonRoadMeters")]
	[InlineData(17, "NonRoadMeters")]
	[InlineData(18, "NonRoadMeters")]
	[InlineData(999, "UnknownMeters")]
	public async Task SurfaceCodes_MapAndPartition(int code, string bucket)
	{
		var route = await Read($$$$"""{"surface":{"values":[[0,2,{{{{code}}}}]]}}""" );
		var evidence = JsonSerializer.SerializeToElement(route.Evidence);
		Assert.True(route.Evidence!.SurfaceSupplied);
		Assert.False(route.Evidence.WaytypeSupplied);
		Assert.InRange(evidence.GetProperty("Surface").GetProperty(bucket).GetDouble(), 222390, 222391);
		Assert.Equal(route.Evidence.GeometryLengthMeters, evidence.GetProperty("Surface").EnumerateObject().Sum(x => x.Value.GetDouble()), 7);
	}

	[Theory]
	[InlineData(0, "UnknownMeters")]
	[InlineData(1, "StateRoadMeters")]
	[InlineData(2, "RoadMeters")]
	[InlineData(3, "StreetMeters")]
	[InlineData(4, "PathMeters")]
	[InlineData(5, "TrackMeters")]
	[InlineData(6, "CyclewayMeters")]
	[InlineData(7, "FootwayMeters")]
	[InlineData(8, "StepsMeters")]
	[InlineData(9, "FerryMeters")]
	[InlineData(10, "ConstructionMeters")]
	[InlineData(999, "UnknownMeters")]
	public async Task WayCodes_MapWithoutAssumingSurface(int code, string bucket)
	{
		var route = await Read($$$$"""{"waytype":{"values":[[0,2,{{{{code}}}}]]}}""" );
		Assert.False(route.Evidence!.SurfaceSupplied);
		Assert.True(route.Evidence.WaytypeSupplied);
		Assert.InRange(JsonSerializer.SerializeToElement(route.Evidence.Ways).GetProperty(bucket).GetDouble(), 222390, 222391);
		Assert.Equal(route.Evidence.GeometryLengthMeters, route.Evidence.Surface.UnknownMeters);
	}

	[Theory]
	[InlineData("null")]
	[InlineData("{}")]
	[InlineData("{\"surface\":null}")]
	[InlineData("{\"surface\":{}}")]
	[InlineData("{\"surface\":{\"values\":null}}")]
	[InlineData("{\"surface\":{\"values\":[]}}")]
	public async Task MissingEvidence_IsUnavailable(string extras)
	{
		var route = await Read(extras);
		Assert.False(route.Evidence!.SurfaceSupplied);
		Assert.Equal(route.Evidence.GeometryLengthMeters, route.Evidence.Surface.UnknownMeters);
	}

	[Theory]
	[InlineData("[]")]
	[InlineData("{\"surface\":[]}")]
	[InlineData("{\"waytype\":{\"values\":[[0,2,-1]]}}")]
	public async Task MalformedFamilies_AreRejected(string extras)
	{
		var error = await Assert.ThrowsAsync<RoutingException>(() => Read(extras));
		Assert.Equal(RoutingFailure.InvalidResponse, error.Failure);
	}
	[Theory]
	[InlineData("[[0,3,3]]")]
	[InlineData("[[0,1,-1]]")]
	[InlineData("[[0,1,3],[0,2,10]]")]
	[InlineData("[[1,2,3],[0,1,3]]")]
	[InlineData("[[0,0,3]]")]
	[InlineData("[[0,1.5,3]]")]
	[InlineData("[[0,1,3,4]]")]
	[InlineData("{}")]
	public async Task MalformedRanges_AreRejected(string ranges)
	{
		var error = await Assert.ThrowsAsync<RoutingException>(() => Read($$$$"""{"surface":{"values":{{{{ranges}}}}}}"""));
		Assert.Equal(RoutingFailure.InvalidResponse, error.Failure);
	}

	[Fact]
	public async Task Evidence_PartitionsGeometryAndPreservesUnknown()
	{
		var route = await Read("""{"surface":{"values":[[0,1,3]],"summary":[{"value":3,"amount":100}]},"waytype":{"values":[[0,1,6],[1,2,999]]}}""");
		var json = JsonSerializer.SerializeToElement(route);
		var evidence = json.GetProperty("Evidence");
		Assert.InRange(evidence.GetProperty("GeometryLengthMeters").GetDouble(), 222390, 222391);
		Assert.InRange(evidence.GetProperty("Surface").GetProperty("PavedMeters").GetDouble(), 111195, 111196);
		Assert.InRange(evidence.GetProperty("Surface").GetProperty("UnknownMeters").GetDouble(), 111195, 111196);
		Assert.InRange(evidence.GetProperty("Ways").GetProperty("CyclewayMeters").GetDouble(), 111195, 111196);
		Assert.Equal(20000, route.DistanceMeters);
	}

	[Fact]
	public async Task Segments_PreserveIndependentBoundariesAndZeroLengthEdges()
	{
		var route = await Read("""{"surface":{"values":[[0,2,3],[2,3,10]]},"waytype":{"values":[[0,1,6],[1,3,2]]}}""", "[[0,0],[1,0],[1,0],[2,0]]");
		var segments = SegmentJson(route);
		Assert.Equal(3, segments.GetArrayLength());
		AssertSegment(segments[0], 0, 1, "Asphalt", "Cycleway");
		AssertSegment(segments[1], 1, 2, "Asphalt", "Road");
		AssertSegment(segments[2], 2, 3, "Unpaved", "Road");
		Assert.InRange(route.Evidence!.Surface.PavedMeters, 111195, 111196);
		Assert.InRange(route.Evidence.Surface.NonRoadMeters, 111195, 111196);
	}

	[Theory]
	[InlineData(0, "Unknown")]
	[InlineData(1, "Paved")]
	[InlineData(2, "Unpaved")]
	[InlineData(3, "Asphalt")]
	[InlineData(4, "Paved")]
	[InlineData(5, "Other")]
	[InlineData(6, "Other")]
	[InlineData(7, "Other")]
	[InlineData(8, "Unpaved")]
	[InlineData(9, "Unpaved")]
	[InlineData(10, "Unpaved")]
	[InlineData(11, "Unpaved")]
	[InlineData(12, "Unpaved")]
	[InlineData(13, "Unpaved")]
	[InlineData(14, "Other")]
	[InlineData(15, "Unpaved")]
	[InlineData(16, "Unpaved")]
	[InlineData(17, "Unpaved")]
	[InlineData(18, "Unpaved")]
	[InlineData(999, "Unknown")]
	public async Task Segments_CoalesceNormalizedPairs(int code, string expected)
	{
		var route = await Read($$$"""{"surface":{"values":[[0,1,{{{code}}}],[1,2,{{{code}}}]]}}""");
		var segments = SegmentJson(route);
		Assert.Equal(1, segments.GetArrayLength());
		AssertSegment(segments[0], 0, 2, expected, "Unknown");
	}

	[Fact]
	public async Task Segments_FillGapsWithoutInferringSurfaceFromWayType()
	{
		var route = await Read("""{"surface":{"values":[[1,2,4]]},"waytype":{"values":[[0,2,6]]}}""");
		var segments = SegmentJson(route);
		AssertSegment(segments[0], 0, 1, "Unknown", "Cycleway");
		AssertSegment(segments[1], 1, 2, "Paved", "Cycleway");
		AssertSegment(SegmentJson(await Read("null"))[0], 0, 2, "Unknown", "Unknown");
	}

	private static JsonElement SegmentJson(RoutedPath route)
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
		var evidence = JsonSerializer.SerializeToElement(route.Evidence, options);
		Assert.True(evidence.TryGetProperty("Segments", out var segments), "Segment evidence must survive parsing.");
		return segments;
	}

	private static void AssertSegment(JsonElement segment, int from, int to, string surface, string way)
	{
		Assert.Equal(from, segment.GetProperty("FromPointIndex").GetInt32());
		Assert.Equal(to, segment.GetProperty("ToPointIndex").GetInt32());
		Assert.Equal(surface, segment.GetProperty("Surface").GetString());
		Assert.Equal(way, segment.GetProperty("WayType").GetString());
	}

	internal static async Task<RoutedPath> Read(string extras, string coordinates = "[[0,0],[1,0],[2,0]]")
	{
		var body = $$$$"""{"type":"FeatureCollection","features":[{"geometry":{"type":"LineString","coordinates":{{{{coordinates}}}}},"properties":{"summary":{"distance":20000,"duration":3600},"extras":{{{{extras}}}}}}]}""";
		using var client = new HttpClient(new OpenRouteServiceProviderTests.StubHandler((_, _) => Task.FromResult(OpenRouteServiceProviderTests.Response(200, body))));
		return await new OpenRouteServiceProvider(client, new() { ApiKey = "test" }).GetRoadLoopAsync(new(0, 0), 20000, 1, TestContext.Current.CancellationToken);
	}
}
