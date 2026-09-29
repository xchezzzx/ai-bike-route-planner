namespace CyclingRoutes.Contracts.RoutePlanning;

public sealed record SurfaceBreakdownResponse(double PavedMeters, double NonRoadMeters, double OtherKnownMeters, double UnknownMeters);
public sealed record WayBreakdownResponse(double UnknownMeters, double StateRoadMeters, double RoadMeters, double StreetMeters,
	double PathMeters, double TrackMeters, double CyclewayMeters, double FootwayMeters, double StepsMeters, double FerryMeters, double ConstructionMeters);
public sealed record RoadQualityResponse(string PolicyVersion, double GeometryLengthMeters, string SurfaceEvidenceState,
	bool WaytypeSupplied, SurfaceBreakdownResponse Surface, WayBreakdownResponse Ways,
	double RepeatedMeters, double SharedStemMeters, double RemainingRepeatedMeters);
