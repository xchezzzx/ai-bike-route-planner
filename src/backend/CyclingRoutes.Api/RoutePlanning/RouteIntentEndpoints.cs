using CyclingRoutes.Application.RoutePlanning;
using CyclingRoutes.Contracts.RoutePlanning;
using CyclingRoutes.Domain.RoutePlanning;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CyclingRoutes.Api.RoutePlanning;

public static class RouteIntentEndpoints
{
	public static RouteHandlerBuilder MapRouteIntentEndpoints(this IEndpointRouteBuilder endpoints) =>
		endpoints.MapPost("/api/route-intents/validate", Validate)
			.WithName("ValidateRouteIntent")
			.WithTags("Route planning")
			.ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

	private static Results<Ok<RouteIntentResponse>, ValidationProblem> Validate(
		RouteIntentRequest request, RouteIntentValidator validator, CancellationToken cancellationToken)
	{
		var result = validator.Validate(request, cancellationToken);
		if (result.Intent is not { } intent)
			return TypedResults.ValidationProblem(new Dictionary<string, string[]>(result.Errors));

		return TypedResults.Ok(new RouteIntentResponse(
			new(intent.Start.Latitude, intent.Start.Longitude),
			intent.Destination is { } destination ? new(destination.Latitude, destination.Longitude) : null,
			intent.Shape == RouteShape.Loop ? "loop" : "pointToPoint",
			intent.Profile == CyclingProfile.Road ? "road" : "gravel",
			intent.Elevation switch
			{
				ElevationPreference.Minimize => "minimize",
				ElevationPreference.Balanced => "balanced",
				ElevationPreference.SeekClimbs => "seekClimbs",
				_ => throw new InvalidOperationException("Unsupported elevation preference.")
			},
			intent.TargetDistance?.Meters,
			intent.TargetDuration?.Ticks / TimeSpan.TicksPerSecond));
	}
}
