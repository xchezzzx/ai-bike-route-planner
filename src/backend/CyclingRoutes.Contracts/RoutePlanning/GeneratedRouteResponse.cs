namespace CyclingRoutes.Contracts.RoutePlanning;

public sealed record RoutePointResponse(double Latitude, double Longitude, double? ElevationMeters);

public sealed record GeneratedRouteResponse(
	IReadOnlyList<RoutePointResponse> Geometry,
	double DistanceMeters,
	double EstimatedDurationSeconds,
	double? AscentMeters,
	double? DescentMeters,
	string Attribution,
	IReadOnlyList<string> Warnings,
	string Gpx,
	IReadOnlyList<RouteSegmentResponse>? Segments = null);
