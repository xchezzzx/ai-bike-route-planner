namespace CyclingRoutes.Contracts.RoutePlanning;

public sealed record RouteCandidateAssessmentResponse(
	double? DistanceDeltaMeters, double? DurationDeltaSeconds, bool TargetsMatched, double Score);

public sealed record RouteCandidateResponse(int Seed, RouteCandidateAssessmentResponse Assessment, GeneratedRouteResponse Route);

public sealed record RouteCandidatesResponse(
	double RequestedLengthMeters,
	IReadOnlyList<string> Assumptions,
	int AttemptedCount,
	IReadOnlyList<string> Warnings,
	IReadOnlyList<RouteCandidateResponse> Candidates);
