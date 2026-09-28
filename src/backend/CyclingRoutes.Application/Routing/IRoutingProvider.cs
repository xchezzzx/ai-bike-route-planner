using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

public interface IRoutingProvider
{
	Task<RoutedPath> GetRoadRouteAsync(GeoCoordinate start, GeoCoordinate destination, CancellationToken cancellationToken);
}
