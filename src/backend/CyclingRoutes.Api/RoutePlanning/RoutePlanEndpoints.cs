using System.Text.Json;
using CyclingRoutes.Application.RoutePlanning;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Contracts.RoutePlanning;

namespace CyclingRoutes.Api.RoutePlanning;

public static class RoutePlanEndpoints
{
	public static RouteHandlerBuilder MapRoutePlanEndpoints(this IEndpointRouteBuilder endpoints) =>
		endpoints.MapPost("/api/routes/plan", Plan).WithName("PlanRoadLoop").WithTags("Route planning")
			.Accepts<RouteIntentRequest>("application/json", "application/*+json").Produces<RoutePlanResponse>()
			.ProducesValidationProblem().ProducesProblem(413).ProducesProblem(415)
			.ProducesProblem(422).ProducesProblem(502).ProducesProblem(503).ProducesProblem(504);

	private static async Task<IResult> Plan(HttpRequest request, RouteIntentValidator validator,
		RoutePlanningService service, CancellationToken cancellationToken)
	{
		var read = await RoutePlanRequestReader.ReadAsync(request, cancellationToken);
		if (read.ErrorStatus is { } status) return TypedResults.Problem(statusCode: status);
		var validation = validator.Validate(read.Request!, cancellationToken);
		if (validation.Intent is not { } intent) return TypedResults.ValidationProblem(new Dictionary<string, string[]>(validation.Errors));
		try
		{
			var result = await service.PlanAsync(intent, cancellationToken);
			var search = result.Search;
			var warnings = search.Warnings.ToList();
			if (search.IncompleteFailure is { } failure) warnings.Add(RoutingProblemMapper.Describe(failure).Code);
			var response = new RoutePlanResponse(new(search.RequestedLengthMeters, search.Assumptions, search.AttemptedCount,
				warnings.ToArray(), search.Candidates.Select(RouteCandidateResponseMapper.ToResponse).ToArray(),
				search.ExcludedCandidates.Select(RouteCandidateResponseMapper.ToResponse).ToArray()), result.AdvisorCallCount,
				Code(result.AdvisorStatus), result.AdvisorFailure is { } advisorFailure ? Code(advisorFailure) : null,
				result.Attempts.Select(x => new RoutePlanAttemptResponse(x.Seed, x.RequestedLengthMeters, Code(x.Outcome), Code(x.Reason),
					x.Failure is { } attemptFailure ? RoutingProblemMapper.Describe(attemptFailure).Code : null)).ToArray());
			cancellationToken.ThrowIfCancellationRequested();
			return TypedResults.Ok(response);
		}
		catch (RoutingException error)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return RoutingProblemMapper.ToProblem(error.Failure);
		}
	}

	private static string Code<T>(T value) where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
