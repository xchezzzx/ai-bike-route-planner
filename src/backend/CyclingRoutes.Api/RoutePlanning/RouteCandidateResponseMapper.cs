using CyclingRoutes.Application.Routing;
using CyclingRoutes.Contracts.RoutePlanning;

namespace CyclingRoutes.Api.RoutePlanning;

internal static class RouteCandidateResponseMapper
{
	public static RouteCandidateResponse ToResponse(GeneratedRouteCandidate candidate)
	{
		var assessment = candidate.Assessment;
		return new(candidate.Seed,
			Assessment(assessment),
			GeneratedRouteResponseMapper.ToResponse(candidate.Route));
	}

	public static ExcludedRouteCandidateResponse ToResponse(ExcludedRouteCandidate candidate) =>
		new(candidate.Seed, candidate.DistanceMeters, candidate.EstimatedDurationSeconds, Assessment(candidate.Assessment), candidate.Reasons);

	private static RouteCandidateAssessmentResponse Assessment(RouteCandidateAssessment assessment) =>
		new(assessment.DistanceDeltaMeters, assessment.DurationDeltaSeconds, assessment.TargetsMatched, assessment.Score,
			assessment.Quality is { } q ? Quality(q) : null);

	private static RoadQualityResponse Quality(RoadQualityAssessment q) => new(q.PolicyVersion, q.GeometryLengthMeters,
		System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(q.SurfaceEvidenceState.ToString()), q.WaytypeSupplied,
		new(q.Surface.PavedMeters, q.Surface.NonRoadMeters, q.Surface.OtherKnownMeters, q.Surface.UnknownMeters),
		new(q.Ways.UnknownMeters, q.Ways.StateRoadMeters, q.Ways.RoadMeters, q.Ways.StreetMeters, q.Ways.PathMeters,
			q.Ways.TrackMeters, q.Ways.CyclewayMeters, q.Ways.FootwayMeters, q.Ways.StepsMeters, q.Ways.FerryMeters, q.Ways.ConstructionMeters),
		q.RepeatedMeters, q.SharedStemMeters, q.RemainingRepeatedMeters);
}
