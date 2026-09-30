using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

public sealed record RoutePoint(GeoCoordinate Position, double? ElevationMeters);

public sealed record RoutedPath(
	IReadOnlyList<RoutePoint> Points,
	double DistanceMeters,
	double EstimatedDurationSeconds,
	double? AscentMeters,
	double? DescentMeters,
	string Attribution,
	RoadEvidence? Evidence = null);

public sealed record GeneratedRoute(RoutedPath Path, string Gpx, IReadOnlyList<string> Warnings, string Name)
{
	public static GeneratedRoute Create(RoutedPath path, RouteIntent intent, IReadOnlyList<string> warnings, RouteNameResolver names)
	{
		var name = names.Resolve(path, intent.Shape, intent.Profile);
		var attributed = name.Attribution is null ? path : path with { Attribution = path.Attribution + "\n" + name.Attribution };
		return new(attributed, GpxWriter.Write(attributed, name.Value), warnings, name.Value);
	}
}
