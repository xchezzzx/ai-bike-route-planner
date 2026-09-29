namespace CyclingRoutes.Contracts.RoutePlanning;

public sealed record RouteCandidateAssessmentResponse(
	double? DistanceDeltaMeters, double? DurationDeltaSeconds, bool TargetsMatched, double Score, RoadQualityResponse? Quality);

public sealed record RouteCandidateResponse(int Seed, RouteCandidateAssessmentResponse Assessment, GeneratedRouteResponse Route);

public sealed record ExcludedRouteCandidateResponse(int Seed, double DistanceMeters, double EstimatedDurationSeconds,
	RouteCandidateAssessmentResponse Assessment, IReadOnlyList<string> Reasons);

public sealed record RouteCandidatesResponse(
	double RequestedLengthMeters,
	IReadOnlyList<string> Assumptions,
	int AttemptedCount,
	IReadOnlyList<string> Warnings,
	IReadOnlyList<RouteCandidateResponse> Candidates,
	IReadOnlyList<ExcludedRouteCandidateResponse> ExcludedCandidates);
