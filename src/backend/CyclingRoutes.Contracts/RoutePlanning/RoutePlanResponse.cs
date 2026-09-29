namespace CyclingRoutes.Contracts.RoutePlanning;

public sealed record RoutePlanAttemptResponse(int Seed, double RequestedLengthMeters, string Outcome, string Reason, string? Failure);

public sealed record RoutePlanResponse(RouteCandidatesResponse Search, int AdvisorCallCount,
	string AdvisorStatus, string? AdvisorFailure, IReadOnlyList<RoutePlanAttemptResponse> Attempts);
