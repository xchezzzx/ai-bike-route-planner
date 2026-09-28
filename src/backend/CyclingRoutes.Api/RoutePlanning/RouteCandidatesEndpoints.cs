using CyclingRoutes.Application.RoutePlanning;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Contracts.RoutePlanning;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CyclingRoutes.Api.RoutePlanning;

public static class RouteCandidatesEndpoints
{
	public static RouteHandlerBuilder MapRouteCandidatesEndpoints(this IEndpointRouteBuilder endpoints) =>
		endpoints.MapPost("/api/routes/candidates", Generate)
			.WithName("GenerateRouteCandidates")
			.WithTags("Route planning")
			.ProducesValidationProblem().ProducesProblem(415).ProducesProblem(422)
			.ProducesProblem(502).ProducesProblem(503).ProducesProblem(504);

	private static async Task<Results<Ok<RouteCandidatesResponse>, ValidationProblem, ProblemHttpResult>> Generate(
		RouteIntentRequest request, RouteIntentValidator validator, RouteCandidateService service, CancellationToken cancellationToken)
	{
		var validation = validator.Validate(request, cancellationToken);
		if (validation.Intent is not { } intent)
			return TypedResults.ValidationProblem(new Dictionary<string, string[]>(validation.Errors));
		try
		{
			var result = await service.GenerateAsync(intent, cancellationToken);
			var warnings = result.Warnings.ToList();
			if (result.IncompleteFailure is { } failure) warnings.Add(RoutingProblemMapper.Describe(failure).Code);
			var candidates = result.Candidates.Select(ToResponse).ToArray();
			cancellationToken.ThrowIfCancellationRequested();
			return TypedResults.Ok(new RouteCandidatesResponse(result.RequestedLengthMeters, result.Assumptions,
				result.AttemptedCount, warnings.ToArray(), candidates));
		}
		catch (RoutingException error)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return RoutingProblemMapper.ToProblem(error.Failure);
		}
	}

	private static RouteCandidateResponse ToResponse(GeneratedRouteCandidate candidate)
	{
		var path = candidate.Route.Path;
		var assessment = candidate.Assessment;
		return new(candidate.Seed,
			new(assessment.DistanceDeltaMeters, assessment.DurationDeltaSeconds, assessment.TargetsMatched, assessment.Score),
			new(path.Points.Select(point => new RoutePointResponse(point.Position.Latitude, point.Position.Longitude, point.ElevationMeters)).ToArray(),
				path.DistanceMeters, path.EstimatedDurationSeconds, path.AscentMeters, path.DescentMeters,
				path.Attribution, candidate.Route.Warnings, candidate.Route.Gpx));
	}
}
