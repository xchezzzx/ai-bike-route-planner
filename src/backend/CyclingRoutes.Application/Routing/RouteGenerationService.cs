using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

public sealed class RouteGenerationService(IRoutingProvider provider, RouteNameResolver names)
{
	public async Task<GeneratedRoute> GenerateAsync(RouteIntent intent, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (intent.Shape != RouteShape.PointToPoint || intent.Profile != CyclingProfile.Road || intent.Elevation != ElevationPreference.Balanced)
			throw new RoutingException(RoutingFailure.UnsupportedIntent);

		var path = await provider.GetRoadRouteAsync(intent.Start, intent.Destination!, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		return GeneratedRoute.Create(path, intent, RouteTargets.DistanceAim(intent) is not null || RouteTargets.DurationAim(intent) is not null
			? ["targets_not_optimized"] : [], names);
	}
}
