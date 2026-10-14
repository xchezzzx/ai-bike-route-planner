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
	public void NearReturn_IsSoftlyRankedBelowCleanRingWithoutChangingExactMetrics()
	{
		var clean = LoopGeometryMetricsTests.Meters((0, 0), (2000, 0), (2000, 2000), (0, 2000), (0, 0));
		var result = new RoadCandidateSelector(new(), new()).Select(Intent(),
			[new(1, LoopGeometryMetricsTests.ParallelReturn()), new(2, clean)], TestContext.Current.CancellationToken);
		Assert.Equal(new[] { 2, 1 }, result.Retained.Select(x => x.Candidate.Seed));
		Assert.Empty(result.Excluded);
		Assert.Contains("road_near_return", result.Retained[1].Warnings);
		Assert.DoesNotContain("road_retracing", result.Retained[1].Warnings);
		Assert.Equal(0, result.Retained[1].Assessment.Quality!.RemainingRepeatedMeters);
	}

	[Fact]
	public void PointToPoint_DoesNotReceiveLoopGeometryPenalty()
	{
		var intent = new RouteIntent(new(0, 0), RouteShape.PointToPoint, CyclingProfile.Road,
			new(40000), TimeSpan.FromSeconds(3600), new(1, 1));
		var result = new RoadCandidateSelector(new(), new()).Select(intent,
			[new(1, LoopGeometryMetricsTests.ParallelReturn())], TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Retained[0].Assessment.Score);
		Assert.DoesNotContain("road_near_return", result.Retained[0].Warnings);
	}

	[Fact]
	public void UnavailableApproximateCheck_RetainsFiniteExactScore()
	{
		var path = LoopGeometryMetricsTests.Meters(Enumerable.Range(0, 900).SelectMany(_ => new (double, double)[]
			{ (0, 0), (100, 0), (100, 100), (0, 100) }).Append((0d, 0d)).ToArray());
		var result = Select(path);
		var retained = Assert.Single(result.Retained);
		Assert.Empty(result.Excluded);
		Assert.True(double.IsFinite(retained.Assessment.Score));
		Assert.True(retained.Assessment.Quality!.RemainingRepeatedMeters > 0);
		Assert.Contains("road_retracing", retained.Warnings);
		Assert.DoesNotContain("road_near_return", retained.Warnings);
	}

	[Fact]
	public void InconsistentEvidence_IsNotTrusted()
	{
		var path = WithSurface(RoadQualityAssessorTests.Path((0, 0), (0, 1), (1, 0), (0, 0)), 0);
		path = path with { Evidence = path.Evidence! with { Surface = new(double.NaN, 0, 0, 0) } };
		Assert.Equal(RoutingFailure.InvalidResponse, Assert.Throws<RoutingException>(() => Select(path)).Failure);
	}

	[Theory]
	[InlineData(ElevationPreference.Minimize, "target", 0.06, 0.2)]
	[InlineData(ElevationPreference.Minimize, "surface", 0.06, 0.2)]
	[InlineData(ElevationPreference.Minimize, "way", 0.06, 0.2)]
	[InlineData(ElevationPreference.SeekClimbs, "target", 0.02, 0.16)]
	[InlineData(ElevationPreference.SeekClimbs, "surface", 0.02, 0.16)]
	[InlineData(ElevationPreference.SeekClimbs, "way", 0.02, 0.16)]
	public void ExcludedHighAscentOutlier_DoesNotChangeEligibleScoresOrOrder(
		ElevationPreference elevation, string exclusion, double firstScore, double secondScore)
	{
		var path = WithSurface(LoopGeometryMetricsTests.Meters((0, 0), (10000, 0), (10000, 10000), (0, 10000), (0, 0)), 0);
		var first = new RouteCandidate(1, path with { DistanceMeters = 40000, AscentMeters = elevation == ElevationPreference.Minimize ? 500 : 100 });
		var second = new RouteCandidate(2, path with { DistanceMeters = 41000, AscentMeters = elevation == ElevationPreference.Minimize ? 100 : 500 });
		var outlier = path with { DistanceMeters = exclusion == "target" ? 200000 : 40000, AscentMeters = 10000 };
		if (exclusion == "surface") outlier = WithSurface(outlier, path.Evidence!.GeometryLengthMeters * 0.1);
		if (exclusion == "way") outlier = outlier with { Evidence = outlier.Evidence! with {
			WaytypeSupplied = true, Ways = new(path.Evidence!.GeometryLengthMeters - 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0) } };
		var intent = new RouteIntent(new(0, 0), RouteShape.Loop, CyclingProfile.Road, new(40000), elevation: elevation);
		var selector = new RoadCandidateSelector(new(), new());
		var baseline = selector.Select(intent, [first, second], TestContext.Current.CancellationToken);
		var result = selector.Select(intent, [first, second, new(3, outlier)], TestContext.Current.CancellationToken);
		Assert.Equal(new[] { 2, 1 }, baseline.Retained.Select(x => x.Candidate.Seed));
		Assert.Equal(new[] { 2, 1 }, result.Retained.Select(x => x.Candidate.Seed));
		Assert.Equal(firstScore, result.Retained[0].Assessment.Score, 10);
		Assert.Equal(secondScore, result.Retained[1].Assessment.Score, 10);
		Assert.Equal(baseline.Retained.Select(x => x.Assessment.Score), result.Retained.Select(x => x.Assessment.Score));
		var excluded = Assert.Single(result.Excluded);
		Assert.Equal(3, excluded.Seed);
		Assert.Contains(exclusion switch { "target" => "targets_not_met", "surface" => "road_surface_limit_exceeded", _ => "road_waytype_excluded" }, excluded.Reasons);
		Assert.True(result.AnyTargetsMatched);
	}

	[Fact]
	public void AllRoadExcluded_KeepFullSetTargetMatchAndDiagnostics()
	{
		var path = WithSurface(LoopGeometryMetricsTests.Meters((0, 0), (10000, 0), (10000, 10000), (0, 10000), (0, 0)), 1000);
		var result = new RoadCandidateSelector(new(), new()).Select(
			new(new(0, 0), RouteShape.Loop, CyclingProfile.Road, new(40000), elevation: ElevationPreference.Minimize),
			[new(1, path with { DistanceMeters = 40000, AscentMeters = null })], TestContext.Current.CancellationToken);
		Assert.Empty(result.Retained);
		Assert.True(result.AnyTargetsMatched);
		var excluded = Assert.Single(result.Excluded);
		Assert.True(excluded.Assessment.TargetsMatched);
		Assert.Equal(0.2, excluded.Assessment.Score, 10);
		Assert.NotNull(excluded.Assessment.Quality);
		Assert.Equal(new[] { "road_surface_limit_exceeded" }, excluded.Reasons);
	}

	internal static RoutedPath WithSurface(RoutedPath path, double nonRoad)
	{
		var length = RouteGeometryMetrics.EdgeLengths(path.Points, default).Sum();
		return path with { Evidence = new(length, true, false, new(length - nonRoad, nonRoad, 0, 0), new(length, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)) };
	}
	private static RouteIntent Intent() => new(new(0, 0), RouteShape.Loop, CyclingProfile.Road, new(40000), TimeSpan.FromSeconds(3600));
	private static RouteSelectionResult Select(RoutedPath path) => new RoadCandidateSelector(new(), new()).Select(Intent(), [new(1, path)], TestContext.Current.CancellationToken);
}
