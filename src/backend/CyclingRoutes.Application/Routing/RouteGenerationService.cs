using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

public sealed class RouteGenerationService(IRoutingProvider provider)
{
	public async Task<GeneratedRoute> GenerateAsync(RouteIntent intent, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (intent.Shape != RouteShape.PointToPoint || intent.Profile != CyclingProfile.Road || intent.Elevation != ElevationPreference.Balanced)
			throw new RoutingException(RoutingFailure.UnsupportedIntent);

		var path = await provider.GetRoadRouteAsync(intent.Start, intent.Destination!, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		return new(path, GpxWriter.Write(path), ["targets_not_optimized"]);
	}
}
