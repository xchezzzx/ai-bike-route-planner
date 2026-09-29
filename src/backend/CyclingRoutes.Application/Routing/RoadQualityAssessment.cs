namespace CyclingRoutes.Application.Routing;

public enum SurfaceEvidenceState { Unavailable, Partial, Complete }

public sealed record RoadQualityAssessment(string PolicyVersion, double GeometryLengthMeters,
	SurfaceEvidenceState SurfaceEvidenceState, bool WaytypeSupplied, SurfaceBreakdown Surface, WayBreakdown Ways,
	double RepeatedMeters, double SharedStemMeters, double RemainingRepeatedMeters);
