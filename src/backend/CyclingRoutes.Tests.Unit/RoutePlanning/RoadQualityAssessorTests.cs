using CyclingRoutes.Application.Routing;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class RoadQualityAssessorTests
{
	[Fact]
	public void SharedStem_IsNotAnInternalRetrace()
	{
		var path = Path((0, 0), (0, 1), (0, 2), (1, 1), (0, 1), (0, 0));
		var result = new RoadQualityAssessor().Assess(path, TestContext.Current.CancellationToken);
		Assert.InRange(result.RepeatedMeters, 111195, 111196);
		Assert.Equal(result.RepeatedMeters, result.SharedStemMeters);
		Assert.Equal(0, result.RemainingRepeatedMeters);
		Assert.Equal(SurfaceEvidenceState.Unavailable, result.SurfaceEvidenceState);
		Assert.Equal(result.GeometryLengthMeters, result.Surface.UnknownMeters);
	}

	[Fact]
	public void InternalAndThirdTraversals_AreCountedWithoutElevation()
	{
		var path = Path((0, 0), (0, 1), (0, 2), (0, 1), (0, 2), (1, 0), (0, 0));
		var result = new RoadQualityAssessor().Assess(path, TestContext.Current.CancellationToken);
		Assert.InRange(result.RepeatedMeters, 222390, 222391);
		Assert.Equal(0, result.SharedStemMeters);
		Assert.Equal(result.RepeatedMeters, result.RemainingRepeatedMeters);
		var reverse = new RoadQualityAssessor().Assess(path with { Points = path.Points.Reverse().ToArray() }, TestContext.Current.CancellationToken);
		Assert.Equal(result.RepeatedMeters, reverse.RepeatedMeters, 6);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Triangle_HasNoRepeats(bool zeroEdge)
	{
		var path = zeroEdge ? Path((0, 0), (0, 0), (0, 1), (1, 0), (0, 0)) : Path((0, 0), (0, 1), (1, 0), (0, 0));
		var result = new RoadQualityAssessor().Assess(path, TestContext.Current.CancellationToken);
		Assert.Equal(0, result.RepeatedMeters);
		Assert.ThrowsAny<OperationCanceledException>(() => new RoadQualityAssessor().Assess(path, new(true)));
	}

	[Fact]
	public void OutAndBack_CapsStemAtOneReturnTraversal()
	{
		var result = new RoadQualityAssessor().Assess(Path((0, 0), (0, 1), (0, 0)), TestContext.Current.CancellationToken);
		Assert.Equal(result.GeometryLengthMeters / 2, result.SharedStemMeters, 6);
		Assert.Equal(0, result.RemainingRepeatedMeters);
	}

	internal static RoutedPath Path(params (double Lat, double Lon)[] coordinates) =>
		new(coordinates.Select((p, i) => new RoutePoint(new(p.Lat, p.Lon), i * 10)).ToArray(), 40000, 3600, 100, 100, "synthetic");
}
