using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

internal static class RouteNamingFixture
{
	public static RouteNameResolver NeutralNames { get; } = new(new EmptyLookup());
	private sealed class EmptyLookup : ISettlementLookup
	{
		public string? Attribution => null;
		public SettlementName? FindNearest(GeoCoordinate position) => null;
	}
}
