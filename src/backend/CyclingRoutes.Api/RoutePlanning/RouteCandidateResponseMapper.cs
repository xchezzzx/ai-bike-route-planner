using CyclingRoutes.Application.Routing;
using CyclingRoutes.Contracts.RoutePlanning;

namespace CyclingRoutes.Api.RoutePlanning;

internal static class RouteCandidateResponseMapper
{
	public static RouteCandidateResponse ToResponse(GeneratedRouteCandidate candidate)
	{
		var path = candidate.Route.Path;
		var assessment = candidate.Assessment;
		return new(candidate.Seed,
			new(assessment.DistanceDeltaMeters, assessment.DurationDeltaSeconds, assessment.TargetsMatched, assessment.Score),
			new(path.Points.Select(point => new RoutePointResponse(point.Position.Latitude, point.Position.Longitude, point.ElevationMeters)).ToArray(),
				path.DistanceMeters, path.EstimatedDurationSeconds, path.AscentMeters, path.DescentMeters,
				path.Attribution, candidate.Route.Warnings, candidate.Route.Gpx));
	}
}
