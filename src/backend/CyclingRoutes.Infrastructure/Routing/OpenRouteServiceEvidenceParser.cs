using System.Text.Json;
using CyclingRoutes.Application.Routing;

namespace CyclingRoutes.Infrastructure.Routing;

internal static class OpenRouteServiceEvidenceParser
{
	public static RoadEvidence Parse(JsonElement properties, IReadOnlyList<RoutePoint> points, CancellationToken cancellationToken)
	{
		var edges = RouteGeometryMetrics.EdgeLengths(points, cancellationToken);
		var length = edges.Sum();
		if (!double.IsFinite(length) || length <= 0) throw Invalid();
		var extras = properties.TryGetProperty("extras", out var value) ? value : default;
		if (extras.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Object)) throw Invalid();
		var surface = Read("surface", 4, SurfaceCategory, out var surfaceSupplied);
		var ways = Read("waytype", 11, code => code <= 10 ? code : 0, out var waySupplied);
		return new(length, surfaceSupplied, waySupplied, new(surface[1], surface[2], surface[3], surface[0]),
			new(ways[0], ways[1], ways[2], ways[3], ways[4], ways[5], ways[6], ways[7], ways[8], ways[9], ways[10]));

		double[] Read(string name, int categories, Func<int, int> category, out bool supplied)
		{
			supplied = false;
			var totals = new double[categories];
			if (extras.ValueKind is not JsonValueKind.Object || !extras.TryGetProperty(name, out var family) || family.ValueKind == JsonValueKind.Null)
			{ totals[0] = length; return totals; }
			if (family.ValueKind != JsonValueKind.Object) throw Invalid();
			if (!family.TryGetProperty("values", out var ranges) || ranges.ValueKind == JsonValueKind.Null)
			{ totals[0] = length; return totals; }
			if (ranges.ValueKind != JsonValueKind.Array) throw Invalid();
			var cursor = 0;
			foreach (var range in ranges.EnumerateArray())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (range.ValueKind != JsonValueKind.Array || range.GetArrayLength() != 3) throw Invalid();
				var start = Integer(range[0]); var end = Integer(range[1]); var code = Integer(range[2]);
				if (start < cursor || end <= start || end >= points.Count) throw Invalid();
				while (cursor < start) Add(0);
				var bucket = category(code);
				while (cursor < end) Add(bucket);
				supplied = true;
			}
			while (cursor < edges.Length) Add(0);
			return totals;

			void Add(int bucket)
			{
				cancellationToken.ThrowIfCancellationRequested();
				totals[bucket] += edges[cursor++];
			}
		}
	}

	private static int SurfaceCategory(int code) => code switch
	{
		1 or 3 or 4 => 1,
		2 or 8 or 9 or 10 or 11 or 12 or 13 or 15 or 16 or 17 or 18 => 2,
		5 or 6 or 7 or 14 => 3,
		_ => 0
	};

	private static int Integer(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0 ? number : throw Invalid();
	private static RoutingException Invalid() => new(RoutingFailure.InvalidResponse);
}
