using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

public interface IRoutingProvider
{
	Task<RoutedPath> GetRoadRouteAsync(GeoCoordinate start, GeoCoordinate destination, CancellationToken cancellationToken);
	Task<RoutedPath> GetRoadLoopAsync(GeoCoordinate start, double requestedLengthMeters, int seed, CancellationToken cancellationToken);
}
