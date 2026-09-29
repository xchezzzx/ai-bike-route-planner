namespace CyclingRoutes.Application.Routing;

public enum RouteAdvisorStatus { NotNeeded, SkippedNoCandidates, SkippedRoutingFailure, Searched, Stopped, Failed }

public sealed record RoutePlanningAttempt(int Seed, double RequestedLengthMeters, RouteSearchOutcome Outcome,
	RouteSearchReason Reason, RoutingFailure? Failure);

public sealed record RoutePlanningResult(RouteCandidateSearchResult Search, int AdvisorCallCount,
	RouteAdvisorStatus AdvisorStatus, RouteSearchAdvisorFailure? AdvisorFailure, IReadOnlyList<RoutePlanningAttempt> Attempts);
