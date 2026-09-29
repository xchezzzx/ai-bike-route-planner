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
		var surface = Read("surface", 4, SurfaceCategory, code => (int)DisplaySurface(code), out var surfaceSupplied, out var surfaceRuns);
		var ways = Read("waytype", 11, WayCategory, WayCategory, out var waySupplied, out var wayRuns);
		var segments = new List<RouteSegment>();
		var s = 0; var w = 0; var from = 0;
		// Intersect the two partitions without duplicating route coordinates.
		while (from < edges.Length)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var end = Math.Min(surfaceRuns[s].End, wayRuns[w].End);
			var item = new RouteSegment(from, end, (RouteSurface)surfaceRuns[s].Code, (RouteWayType)wayRuns[w].Code);
			if (segments.Count > 0 && segments[^1].Surface == item.Surface && segments[^1].WayType == item.WayType)
				segments[^1] = segments[^1] with { ToPointIndex = end };
			else segments.Add(item);
			if (surfaceRuns[s].End == end) s++;
			if (wayRuns[w].End == end) w++;
			from = end;
		}
		return new(length, surfaceSupplied, waySupplied, new(surface[1], surface[2], surface[3], surface[0]),
			new(ways[0], ways[1], ways[2], ways[3], ways[4], ways[5], ways[6], ways[7], ways[8], ways[9], ways[10]), segments.AsReadOnly());

		double[] Read(string name, int categories, Func<int, int> category, Func<int, int> displayCategory,
			out bool supplied, out List<(int End, int Code)> resultRuns)
		{
			supplied = false;
			var runs = new List<(int End, int Code)>();
			resultRuns = runs;
			var totals = new double[categories];
			if (extras.ValueKind is not JsonValueKind.Object || !extras.TryGetProperty(name, out var family) || family.ValueKind == JsonValueKind.Null)
			{ totals[0] = length; runs.Add((edges.Length, 0)); return totals; }
			if (family.ValueKind != JsonValueKind.Object) throw Invalid();
			if (!family.TryGetProperty("values", out var ranges) || ranges.ValueKind == JsonValueKind.Null)
			{ totals[0] = length; runs.Add((edges.Length, 0)); return totals; }
			if (ranges.ValueKind != JsonValueKind.Array) throw Invalid();
			var cursor = 0;
			foreach (var range in ranges.EnumerateArray())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (range.ValueKind != JsonValueKind.Array || range.GetArrayLength() != 3) throw Invalid();
				var start = Integer(range[0]); var end = Integer(range[1]); var code = Integer(range[2]);
				if (start < cursor || end <= start || end >= points.Count) throw Invalid();
				while (cursor < start) Add(0, 0);
				var bucket = category(code);
				while (cursor < end) Add(bucket, displayCategory(code));
				supplied = true;
			}
			while (cursor < edges.Length) Add(0, 0);
			return totals;

			void Add(int bucket, int display)
			{
				cancellationToken.ThrowIfCancellationRequested();
				totals[bucket] += edges[cursor++];
				if (runs.Count > 0 && runs[^1].Code == display) runs[^1] = (cursor, display);
				else runs.Add((cursor, display));
			}
		}
	}

	private static int WayCategory(int code) => code <= 10 ? code : 0;
	private static RouteSurface DisplaySurface(int code) => code == 3 ? RouteSurface.Asphalt : SurfaceCategory(code) switch
	{
		1 => RouteSurface.Paved, 2 => RouteSurface.Unpaved, 3 => RouteSurface.Other, _ => RouteSurface.Unknown
	};

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
