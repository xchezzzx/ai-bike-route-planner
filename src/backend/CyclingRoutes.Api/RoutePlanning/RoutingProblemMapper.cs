using CyclingRoutes.Application.Routing;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CyclingRoutes.Api.RoutePlanning;

internal static class RoutingProblemMapper
{
	internal static (int Status, string Code) Describe(RoutingFailure failure) => failure switch
	{
		RoutingFailure.NotConfigured => (503, "routing_not_configured"),
		RoutingFailure.CredentialsRejected => (503, "routing_credentials_rejected"),
		RoutingFailure.UnsupportedIntent => (422, "unsupported_intent"),
		RoutingFailure.NoRoute => (422, "route_not_found"),
		RoutingFailure.RateLimited => (503, "routing_rate_limited"),
		RoutingFailure.Unavailable => (503, "routing_unavailable"),
		RoutingFailure.Timeout => (504, "routing_timeout"),
		RoutingFailure.InvalidResponse => (502, "routing_invalid_response"),
		RoutingFailure.SearchDistanceOutOfRange => (422, "search_distance_out_of_range"),
		RoutingFailure.LimitExceeded => (422, "routing_limit_exceeded"),
		_ => throw new InvalidOperationException("Unknown routing failure.")
	};

	internal static ProblemHttpResult ToProblem(RoutingFailure failure)
	{
		var (status, code) = Describe(failure);
		return TypedResults.Problem(statusCode: status, title: "Route generation failed.",
			extensions: new Dictionary<string, object?> { ["code"] = code });
	}
}
