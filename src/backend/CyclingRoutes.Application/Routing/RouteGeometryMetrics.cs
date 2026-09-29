using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

public static class RouteGeometryMetrics
{
	public static double DistanceMeters(GeoCoordinate a, GeoCoordinate b)
	{
		const double radians = Math.PI / 180;
		var dlat = (b.Latitude - a.Latitude) * radians;
		var dlon = (b.Longitude - a.Longitude) * radians;
		var h = Math.Pow(Math.Sin(dlat / 2), 2) + Math.Cos(a.Latitude * radians) * Math.Cos(b.Latitude * radians) * Math.Pow(Math.Sin(dlon / 2), 2);
		return 6371008.8 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(h, 0, 1)));
	}

	public static double[] EdgeLengths(IReadOnlyList<RoutePoint> points, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var result = new double[Math.Max(0, points.Count - 1)];
		for (var i = 0; i < result.Length; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			result[i] = DistanceMeters(points[i].Position, points[i + 1].Position);
		}
		return result;
	}
}
