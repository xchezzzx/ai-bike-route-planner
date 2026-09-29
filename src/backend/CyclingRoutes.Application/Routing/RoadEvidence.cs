namespace CyclingRoutes.Application.Routing;

public sealed record SurfaceBreakdown(double PavedMeters, double NonRoadMeters, double OtherKnownMeters, double UnknownMeters);

public sealed record WayBreakdown(double UnknownMeters, double StateRoadMeters, double RoadMeters, double StreetMeters,
	double PathMeters, double TrackMeters, double CyclewayMeters, double FootwayMeters, double StepsMeters,
	double FerryMeters, double ConstructionMeters);

public sealed record RoadEvidence(double GeometryLengthMeters, bool SurfaceSupplied, bool WaytypeSupplied,
	SurfaceBreakdown Surface, WayBreakdown Ways);
