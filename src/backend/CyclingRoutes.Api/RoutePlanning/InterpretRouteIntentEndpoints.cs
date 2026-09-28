using CyclingRoutes.Application.Interpretation;
using CyclingRoutes.Contracts.RoutePlanning;

namespace CyclingRoutes.Api.RoutePlanning;

public static class InterpretRouteIntentEndpoints
{
	public static RouteHandlerBuilder MapInterpretRouteIntentEndpoints(this IEndpointRouteBuilder endpoints) =>
		endpoints.MapPost("/api/route-intents/interpret", Interpret)
			.WithName("InterpretRouteIntent").WithTags("Route planning")
			.Accepts<InterpretRouteIntentRequest>("application/json", "application/*+json")
			.Produces<InterpretRouteIntentResponse>()
			.ProducesValidationProblem().ProducesProblem(413).ProducesProblem(415)
			.ProducesProblem(422).ProducesProblem(502).ProducesProblem(503).ProducesProblem(504);

	private static async Task<IResult> Interpret(HttpRequest request, InterpretationService service, CancellationToken cancellationToken)
	{
		var read = await InterpretRequestReader.ReadAsync(request, cancellationToken);
		if (read.ErrorStatus is { } status) return TypedResults.Problem(statusCode: status);
		try
		{
			var result = await service.InterpretAsync(read.Request!, cancellationToken);
			if (result.Response is { } response) return TypedResults.Ok(response);
			return TypedResults.ValidationProblem(new Dictionary<string, string[]>(result.Errors));
		}
		catch (InterpretationException error)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return InterpretationProblemMapper.ToProblem(error.Failure);
		}
	}
}
