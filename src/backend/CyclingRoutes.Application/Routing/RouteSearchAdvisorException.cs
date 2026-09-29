namespace CyclingRoutes.Application.Routing;

public enum RouteSearchAdvisorFailure { NotConfigured, Authentication, Quota, Unavailable, Timeout, InvalidResponse }

public sealed class RouteSearchAdvisorException(RouteSearchAdvisorFailure failure) : Exception("Route search advisor failed.")
{
	public RouteSearchAdvisorFailure Failure { get; } = failure;
}
