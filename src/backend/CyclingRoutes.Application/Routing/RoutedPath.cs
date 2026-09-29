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

public sealed record GeneratedRoute(RoutedPath Path, string Gpx, IReadOnlyList<string> Warnings);
