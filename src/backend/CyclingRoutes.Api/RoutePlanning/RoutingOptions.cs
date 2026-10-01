using CyclingRoutes.Infrastructure.Routing;

namespace CyclingRoutes.Api.RoutePlanning;

public sealed class RoutingOptions
{
	public string Provider { get; set; } = "OpenRouteService";
	public GraphHopperOptions GraphHopper { get; set; } = new();

	public bool IsValid() => Provider == "OpenRouteService"
		|| (Provider == "GraphHopper" && GraphHopper.IsValid());
}
