namespace CyclingRoutes.Contracts.RoutePlanning;

public sealed record ClarificationResponse(string Field, string Code, string Message);
public sealed record InterpretRouteIntentResponse(string Status, RouteIntentRequest Draft,
	RouteIntentResponse? Intent, IReadOnlyList<ClarificationResponse> Clarifications,
	IReadOnlyList<string> Limitations, IReadOnlyList<string> Assumptions);
