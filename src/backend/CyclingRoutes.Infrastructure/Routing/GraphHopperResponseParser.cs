using System.Text.Json;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Infrastructure.Routing;

internal static class GraphHopperResponseParser
{
	public static RoutedPath Parse(string body, CancellationToken cancellationToken)
	{
		try
		{
			using var json = JsonDocument.Parse(body);
			var paths = json.RootElement.GetProperty("paths");
			if (paths.GetArrayLength() == 0) throw Invalid();
			var path = paths[0];
			if (path.TryGetProperty("points_encoded", out var encoded) && encoded.GetBoolean()) throw Invalid();
			var geometry = path.GetProperty("points");
			if (geometry.GetProperty("type").GetString() != "LineString") throw Invalid();
			var points = new List<RoutePoint>();
			foreach (var coordinate in geometry.GetProperty("coordinates").EnumerateArray())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (coordinate.GetArrayLength() is not (2 or 3)) throw Invalid();
				points.Add(new(new GeoCoordinate(Number(coordinate[1]), Number(coordinate[0])),
					coordinate.GetArrayLength() == 3 ? Number(coordinate[2]) : null));
			}
			if (points.Count < 2) throw Invalid();
			var distance = Number(path.GetProperty("distance"));
			var seconds = Number(path.GetProperty("time")) / 1000;
			if (distance <= 0 || seconds <= 0) throw Invalid();
			return new(points.AsReadOnly(), distance, seconds, Metric(path, "ascend"), Metric(path, "descend"),
				"GraphHopper (self-hosted) | OpenStreetMap contributors", GraphHopperEvidenceParser.Parse(path, points, cancellationToken));
		}
		catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
		{
			throw Invalid();
		}
	}

	private static double Number(JsonElement value)
	{
		if (!value.TryGetDouble(out var number) || !double.IsFinite(number)) throw Invalid();
		return number;
	}

	private static double? Metric(JsonElement path, string name)
	{
		if (!path.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
		var number = Number(value);
		if (number < 0) throw Invalid();
		return number;
	}

	private static RoutingException Invalid() => new(RoutingFailure.InvalidResponse);
}
