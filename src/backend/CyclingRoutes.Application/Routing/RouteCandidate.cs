namespace CyclingRoutes.Application.Routing;

public sealed record RouteCandidate(int Seed, RoutedPath Path);

public sealed record RouteCandidateAssessment(
	double? DistanceDeltaMeters, double? DurationDeltaSeconds, bool TargetsMatched, double Score);

public sealed record RankedRouteCandidate(
	RouteCandidate Candidate, RouteCandidateAssessment Assessment, IReadOnlyList<string> Warnings);

public sealed record GeneratedRouteCandidate(int Seed, RouteCandidateAssessment Assessment, GeneratedRoute Route);

public sealed record RouteCandidateSearchResult(
	double RequestedLengthMeters,
	IReadOnlyList<string> Assumptions,
	int AttemptedCount,
	IReadOnlyList<string> Warnings,
	IReadOnlyList<GeneratedRouteCandidate> Candidates,
	RoutingFailure? IncompleteFailure);
