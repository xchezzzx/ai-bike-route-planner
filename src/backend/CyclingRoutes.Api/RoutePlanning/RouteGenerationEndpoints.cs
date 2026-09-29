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
			return TypedResults.Ok(GeneratedRouteResponseMapper.ToResponse(result));
		}
		catch (RoutingException error)
		{
			return RoutingProblemMapper.ToProblem(error.Failure);
		}
	}
}
