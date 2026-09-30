using System.Text.Json;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Contracts.RoutePlanning;

namespace CyclingRoutes.Api.RoutePlanning;

internal static class GeneratedRouteResponseMapper
{
	public static GeneratedRouteResponse ToResponse(GeneratedRoute route)
	{
		var path = route.Path;
		IReadOnlyList<RouteSegment> segments = path.Evidence?.Segments is { Count: > 0 } supplied
			? supplied : [new(0, path.Points.Count - 1, RouteSurface.Unknown, RouteWayType.Unknown)];
		return new(path.Points.Select(point => new RoutePointResponse(point.Position.Latitude, point.Position.Longitude, point.ElevationMeters)).ToArray(),
			path.DistanceMeters, path.EstimatedDurationSeconds, path.AscentMeters, path.DescentMeters,
			path.Attribution, route.Warnings, route.Gpx, route.Name,
			segments.Select(segment => new RouteSegmentResponse(segment.FromPointIndex, segment.ToPointIndex,
				JsonNamingPolicy.CamelCase.ConvertName(segment.Surface.ToString()),
				JsonNamingPolicy.CamelCase.ConvertName(segment.WayType.ToString()))).ToArray());
	}
}
