namespace CyclingRoutes.Application.Routing;

public enum RoutingFailure
{
	NotConfigured,
	CredentialsRejected,
	UnsupportedIntent,
	NoRoute,
	RateLimited,
	Unavailable,
	Timeout,
	InvalidResponse
}

public sealed class RoutingException(RoutingFailure failure) : Exception($"Routing failed: {failure}.")
{
	public RoutingFailure Failure { get; } = failure;
}
