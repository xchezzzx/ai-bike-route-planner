using CyclingRoutes.Infrastructure.Interpretation;
using CyclingRoutes.Infrastructure.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace CyclingRoutes.Tests.Integration;

internal static class OfflineProviders
{
	public static void Configure(IServiceCollection services)
	{
		// Program snapshots options before test configuration; override the services too.
		services.AddSingleton(new GeminiOptions());
		services.AddSingleton(new OpenRouteServiceOptions());
		services.PostConfigureAll<HttpClientFactoryOptions>(options =>
			options.HttpMessageHandlerBuilderActions.Add(http => http.PrimaryHandler = new NoNetworkHandler()));
	}

	private sealed class NoNetworkHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			throw new InvalidOperationException("Outbound HTTP is forbidden in offline tests.");
	}
}
