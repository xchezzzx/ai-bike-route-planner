namespace CyclingRoutes.Application.Interpretation;

public sealed record ExtractionIssue(string Field, string Code);
public sealed record RouteIntentExtraction(string? Shape, string? Profile, string? Elevation,
	double? TargetDistanceMeters, long? TargetDurationSeconds, IReadOnlyList<ExtractionIssue> Issues);
