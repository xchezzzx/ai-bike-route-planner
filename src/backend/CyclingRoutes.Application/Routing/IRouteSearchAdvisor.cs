namespace CyclingRoutes.Application.Routing;

public interface IRouteSearchAdvisor
{
	Task<RouteSearchAdvice> AdviseAsync(RouteSearchContext context, CancellationToken cancellationToken);
}
