using CyclingRoutes.Application.Routing;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class RouteGeometryMetricsTests
{
	[Fact]
	public void Distance_UsesMetresAndIsSymmetric()
	{
		Assert.InRange(RouteGeometryMetrics.DistanceMeters(new(0, 0), new(0, 1)), 111190, 111200);
		Assert.Equal(0, RouteGeometryMetrics.DistanceMeters(new(32, 34), new(32, 34)));
		Assert.Equal(RouteGeometryMetrics.DistanceMeters(new(32, 34), new(33, 35)), RouteGeometryMetrics.DistanceMeters(new(33, 35), new(32, 34)), 8);
		Assert.InRange(RouteGeometryMetrics.DistanceMeters(new(0, 0), new(0, 180)), 20015000, 20016000);
	}

	[Fact]
	public void EdgeLengths_IgnoreElevationAndObserveCancellation()
	{
		RoutePoint[] points = [new(new(0, 0), 100), new(new(0, 0), 500), new(new(0, 1), null)];
		var lengths = RouteGeometryMetrics.EdgeLengths(points, TestContext.Current.CancellationToken);
		Assert.Equal(0, lengths[0]);
		Assert.InRange(lengths[1], 111190, 111200);
		Assert.ThrowsAny<OperationCanceledException>(() => RouteGeometryMetrics.EdgeLengths(points, new(true)));
	}
}
