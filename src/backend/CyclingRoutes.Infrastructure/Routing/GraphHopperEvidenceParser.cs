using System.Text.Json;
using CyclingRoutes.Application.Routing;

namespace CyclingRoutes.Infrastructure.Routing;

internal static class GraphHopperEvidenceParser
{
	public static RoadEvidence Parse(JsonElement path, IReadOnlyList<RoutePoint> points, CancellationToken cancellationToken)
	{
		var edges = RouteGeometryMetrics.EdgeLengths(points, cancellationToken);
		var length = edges.Sum();
		if (!double.IsFinite(length) || length <= 0) throw Invalid();
		var details = path.TryGetProperty("details", out var value) ? value : default;
		if (details.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Object)) throw Invalid();
		var surfaces = Read("surface", Surface, RouteSurface.Unknown, out var surfaceSupplied);
		var ways = Read("road_class", Way, RouteWayType.Unknown, out var classSupplied);
		var environments = Read("road_environment", s => s == "ferry", false, out var environmentSupplied);
		var surfaceTotals = new double[4];
		var wayTotals = new double[11];
		var segments = new List<RouteSegment>();
		for (var i = 0; i < edges.Length; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var way = environments[i] ? RouteWayType.Ferry : ways[i];
			var bucket = surfaces[i] switch
			{
				RouteSurface.Asphalt or RouteSurface.Paved => 1,
				RouteSurface.Unpaved => 2,
				RouteSurface.Other => 3,
				_ => 0
			};
			surfaceTotals[bucket] += edges[i];
			wayTotals[(int)way] += edges[i];
			if (segments.Count > 0 && segments[^1].Surface == surfaces[i] && segments[^1].WayType == way)
				segments[^1] = segments[^1] with { ToPointIndex = i + 1 };
			else segments.Add(new(i, i + 1, surfaces[i], way));
		}
		return new(length, surfaceSupplied, classSupplied || (environmentSupplied && environments.Any(x => x)),
			new(surfaceTotals[1], surfaceTotals[2], surfaceTotals[3], surfaceTotals[0]),
			new(wayTotals[0], wayTotals[1], wayTotals[2], wayTotals[3], wayTotals[4], wayTotals[5], wayTotals[6],
				wayTotals[7], wayTotals[8], wayTotals[9], wayTotals[10]), segments.AsReadOnly());

		T[] Read<T>(string name, Func<string?, T> classify, T unknown, out bool supplied)
		{
			supplied = false;
			var result = Enumerable.Repeat(unknown, edges.Length).ToArray();
			if (details.ValueKind != JsonValueKind.Object || !details.TryGetProperty(name, out var ranges) || ranges.ValueKind == JsonValueKind.Null)
				return result;
			if (ranges.ValueKind != JsonValueKind.Array) throw Invalid();
			var cursor = 0;
			foreach (var range in ranges.EnumerateArray())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (range.ValueKind != JsonValueKind.Array || range.GetArrayLength() != 3
					|| !range[0].TryGetInt32(out var start) || !range[1].TryGetInt32(out var end)
					|| start < cursor || end <= start || end > edges.Length
					|| range[2].ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw Invalid();
				var category = classify(range[2].GetString());
				for (var i = start; i < end; i++) { cancellationToken.ThrowIfCancellationRequested(); result[i] = category; }
				cursor = end;
				supplied = true;
			}
			return result;
		}
	}

	private static RouteSurface Surface(string? value) => value switch
	{
		"asphalt" => RouteSurface.Asphalt,
		"paved" or "concrete" or "paving_stones" or "cobblestone" => RouteSurface.Paved,
		"unpaved" or "compacted" or "fine_gravel" or "gravel" or "ground" or "dirt" or "grass" or "sand" => RouteSurface.Unpaved,
		"wood" or "other" => RouteSurface.Other,
		_ => RouteSurface.Unknown
	};

	private static RouteWayType Way(string? value) => value switch
	{
		"motorway" or "trunk" or "primary" or "secondary" or "tertiary" or "unclassified" or "road" => RouteWayType.Road,
		"residential" or "living_street" or "service" => RouteWayType.Street,
		"cycleway" => RouteWayType.Cycleway,
		"path" or "bridleway" => RouteWayType.Path,
		"track" => RouteWayType.Track,
		"footway" or "pedestrian" => RouteWayType.Footway,
		"steps" => RouteWayType.Steps,
		"construction" => RouteWayType.Construction,
		_ => RouteWayType.Unknown
	};

	private static RoutingException Invalid() => new(RoutingFailure.InvalidResponse);
}
