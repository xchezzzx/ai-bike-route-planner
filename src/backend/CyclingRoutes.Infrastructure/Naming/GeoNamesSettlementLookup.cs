using System.Text.Json;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Infrastructure.Naming;

public sealed class GeoNamesSettlementLookup : ISettlementLookup
{
	private readonly (SettlementName Name, GeoCoordinate Position)[] _settlements;
	public string Attribution => "Settlement names derived from GeoNames (https://www.geonames.org/), CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/); filtered IL extract.";

	public GeoNamesSettlementLookup(Stream? data)
	{
		_settlements = [];
		if (data is null) return;
		try
		{
			var document = JsonSerializer.Deserialize<Gazetteer>(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
			if (document?.SchemaVersion != 1 || document.Settlements is null) return;
			var ids = new HashSet<long>();
			var entries = new List<(SettlementName, GeoCoordinate)>();
			foreach (var entry in document.Settlements)
			{
				if (entry is null || entry.Id <= 0 || !ids.Add(entry.Id) || string.IsNullOrWhiteSpace(entry.AsciiName)
					|| entry.AsciiName.Length > 200 || entry.AsciiName.Any(c => c is < ' ' or > '~')
					|| !entry.AsciiName.Any(char.IsAsciiLetterOrDigit) || entry.Latitude is null || entry.Longitude is null) return;
				entries.Add((new(entry.Id, entry.AsciiName), new(entry.Latitude.Value, entry.Longitude.Value)));
			}
			_settlements = entries.OrderBy(entry => entry.Item1.Id).ToArray();
		}
		catch (Exception error) when (error is JsonException or IOException or ArgumentException or NotSupportedException)
		{
			// Naming is optional: an unavailable/invalid local resource must not fail routing.
		}
	}

	public static GeoNamesSettlementLookup LoadEmbedded()
	{
		using var data = typeof(GeoNamesSettlementLookup).Assembly.GetManifestResourceStream("CyclingRoutes.GeoNames.IL.json");
		return new(data);
	}

	public SettlementName? FindNearest(GeoCoordinate position)
	{
		SettlementName? nearest = null;
		// One micrometre accommodates haversine round-off at the inclusive boundary.
		var minimum = 10000d + 1e-6;
		foreach (var entry in _settlements)
		{
			var distance = RouteGeometryMetrics.DistanceMeters(position, entry.Position);
			if (distance > minimum || (nearest is not null && distance == minimum)) continue;
			minimum = distance;
			nearest = entry.Name;
		}
		return nearest;
	}

	private sealed record Gazetteer(int SchemaVersion, Entry?[]? Settlements);
	private sealed record Entry(long Id, string? AsciiName, double? Latitude, double? Longitude);
}
