using CyclingRoutes.Application.RoutePlanning;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Contracts.RoutePlanning;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CyclingRoutes.Api.RoutePlanning;

public static class RouteGenerationEndpoints
{
	public static RouteHandlerBuilder MapRouteGenerationEndpoints(this IEndpointRouteBuilder endpoints) =>
		endpoints.MapPost("/api/routes/generate", Generate)
			.WithName("GenerateRoute")
			.WithTags("Route planning")
			.ProducesProblem(422).ProducesProblem(502).ProducesProblem(503).ProducesProblem(504).ProducesProblem(415);

	private static async Task<Results<Ok<GeneratedRouteResponse>, ValidationProblem, ProblemHttpResult>> Generate(
		RouteIntentRequest request, RouteIntentValidator validator, RouteGenerationService service, CancellationToken cancellationToken)
	{
		var validation = validator.Validate(request, cancellationToken);
		if (validation.Intent is not { } intent)
			return TypedResults.ValidationProblem(new Dictionary<string, string[]>(validation.Errors));
		try
		{
			var result = await service.GenerateAsync(intent, cancellationToken);
			var path = result.Path;
			return TypedResults.Ok(new GeneratedRouteResponse(
				path.Points.Select(point => new RoutePointResponse(point.Position.Latitude, point.Position.Longitude, point.ElevationMeters)).ToArray(),
				path.DistanceMeters, path.EstimatedDurationSeconds, path.AscentMeters, path.DescentMeters,
				path.Attribution, result.Warnings, result.Gpx));
		}
		catch (RoutingException error)
		{
			var (status, code) = error.Failure switch
			{
				RoutingFailure.NotConfigured => (503, "routing_not_configured"),
				RoutingFailure.CredentialsRejected => (503, "routing_credentials_rejected"),
				RoutingFailure.UnsupportedIntent => (422, "unsupported_intent"),
				RoutingFailure.NoRoute => (422, "route_not_found"),
				RoutingFailure.RateLimited => (503, "routing_rate_limited"),
				RoutingFailure.Unavailable => (503, "routing_unavailable"),
				RoutingFailure.Timeout => (504, "routing_timeout"),
				RoutingFailure.InvalidResponse => (502, "routing_invalid_response"),
				_ => throw new InvalidOperationException("Unknown routing failure.")
			};
			return TypedResults.Problem(statusCode: status, title: "Route generation failed.",
				extensions: new Dictionary<string, object?> { ["code"] = code });
		}
	}
}
