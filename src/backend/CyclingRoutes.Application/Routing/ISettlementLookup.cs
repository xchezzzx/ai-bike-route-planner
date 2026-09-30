using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

public sealed record SettlementName(long Id, string AsciiName);

/// <summary>Local, bounded lookup; missing coverage returns null, never performs network I/O.</summary>
public interface ISettlementLookup
{
	SettlementName? FindNearest(GeoCoordinate position);
	string? Attribution { get; }
}
