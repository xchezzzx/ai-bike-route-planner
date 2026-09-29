using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class RoadCandidateSelectorTests
{
	[Theory]
	[InlineData(0, true)]
	[InlineData(0.01, false)]
	public void SurfaceLimit_IsInclusiveAndUsesGeometry(double above, bool retained)
	{
		var path = RoadQualityAssessorTests.Path((0, 0), (0, 1), (1, 0), (0, 0));
		var length = RouteGeometryMetrics.EdgeLengths(path.Points, TestContext.Current.CancellationToken).Sum();
		path = WithSurface(path, length * 0.005 + above);
		var result = Select(path);
		Assert.Equal(retained ? 1 : 0, result.Retained.Count);
		if (retained) Assert.Contains("road_surface_non_road", result.Retained[0].Warnings);
		else Assert.Contains("road_surface_limit_exceeded", Assert.Single(result.Excluded).Reasons);
	}

	[Theory]
	[InlineData(100, true)]
	[InlineData(100.01, false)]
	public void ShortRoute_UsesAbsoluteFloor(double nonRoad, bool retained)
	{
		var path = WithSurface(RoadQualityAssessorTests.Path((0, 0), (0, .01), (.01, 0), (0, 0)), nonRoad);
		Assert.Equal(retained ? 1 : 0, Select(path).Retained.Count);
	}

	[Theory]
	[InlineData(36000, 3600, true)]
	[InlineData(44000, 3960, true)]
	[InlineData(44000.01, 3600, false)]
	[InlineData(40000, 3960.01, false)]
	public void TargetsAndUnknownSurface_AreIndependent(double distance, double seconds, bool retained)
	{
		var result = Select(RoadQualityAssessorTests.Path((0, 0), (0, 1), (1, 0), (0, 0)) with { DistanceMeters = distance, EstimatedDurationSeconds = seconds });
		Assert.Equal(retained, result.AnyTargetsMatched);
		Assert.Equal(retained ? 1 : 0, result.Retained.Count);
		if (retained) Assert.Contains("road_surface_unknown", result.Retained[0].Warnings);
		else Assert.Contains("targets_not_met", result.Excluded[0].Reasons);
	}

	[Theory]
	[InlineData("steps", false)]
	[InlineData("ferry", false)]
	[InlineData("construction", false)]
	[InlineData("track", true)]
	public void WaytypeDoesNotPretendToBeSurface(string kind, bool retained)
	{
		var path = WithSurface(RoadQualityAssessorTests.Path((0, 0), (0, 1), (1, 0), (0, 0)), 0);
		var length = path.Evidence!.GeometryLengthMeters;
		path = path with { Evidence = path.Evidence with { Ways = new(length - 1, 0, 0, 0, 0, kind == "track" ? 1 : 0, 0, 0, kind == "steps" ? 1 : 0, kind == "ferry" ? 1 : 0, kind == "construction" ? 1 : 0), WaytypeSupplied = true } };
		var result = Select(path);
		Assert.Equal(retained ? 1 : 0, result.Retained.Count);
		if (retained) Assert.Contains("road_track_present", result.Retained[0].Warnings);
		else Assert.Contains("road_waytype_excluded", result.Excluded[0].Reasons);
	}

	[Fact]
	public void RetracingChangesReportedScoreAndOrder()
	{
		var simple = RoadQualityAssessorTests.Path((0, 0), (0, 1), (1, 0), (0, 0));
		var retraced = RoadQualityAssessorTests.Path((0, 0), (0, 1), (0, 2), (0, 1), (1, 0), (0, 0));
		var result = new RoadCandidateSelector(new(), new()).Select(Intent(), [new(1, retraced), new(3, simple), new(2, simple)], TestContext.Current.CancellationToken);
		Assert.Equal(new[] { 2, 3, 1 }, result.Retained.Select(x => x.Candidate.Seed));
		Assert.Equal(0, result.Retained[0].Assessment.Score);
		Assert.True(result.Retained[2].Assessment.Score > 0);
		Assert.Contains("road_retracing", result.Retained[2].Warnings);
		Assert.ThrowsAny<OperationCanceledException>(() => new RoadCandidateSelector(new(), new()).Select(Intent(), [], new(true)));
	}

	[Fact]
	public void InconsistentEvidence_IsNotTrusted()
	{
		var path = WithSurface(RoadQualityAssessorTests.Path((0, 0), (0, 1), (1, 0), (0, 0)), 0);
		path = path with { Evidence = path.Evidence! with { Surface = new(double.NaN, 0, 0, 0) } };
		Assert.Equal(RoutingFailure.InvalidResponse, Assert.Throws<RoutingException>(() => Select(path)).Failure);
	}

	internal static RoutedPath WithSurface(RoutedPath path, double nonRoad)
	{
		var length = RouteGeometryMetrics.EdgeLengths(path.Points, default).Sum();
		return path with { Evidence = new(length, true, false, new(length - nonRoad, nonRoad, 0, 0), new(length, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)) };
	}
	private static RouteIntent Intent() => new(new(0, 0), RouteShape.Loop, CyclingProfile.Road, new(40000), TimeSpan.FromSeconds(3600));
	private static RouteSelectionResult Select(RoutedPath path) => new RoadCandidateSelector(new(), new()).Select(Intent(), [new(1, path)], TestContext.Current.CancellationToken);
}
