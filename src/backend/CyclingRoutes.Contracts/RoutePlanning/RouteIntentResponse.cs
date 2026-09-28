namespace CyclingRoutes.Contracts.RoutePlanning;

public sealed record CoordinateResponse(double Latitude, double Longitude);

public sealed record RouteIntentResponse(
	CoordinateResponse Start,
	CoordinateResponse? Destination,
	string Shape,
	string Profile,
	string Elevation,
	double? TargetDistanceMeters,
	long? TargetDurationSeconds);
