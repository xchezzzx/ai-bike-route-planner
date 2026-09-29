using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

public sealed class RoadCandidateSelector(RouteCandidateRanker ranker, RoadQualityAssessor assessor)
{
	private const double NonRoadMinimumMeters = 100;
	private const double NonRoadFraction = 0.005;
	private const double RepeatWarningFraction = 0.05;
	private const double RepeatScoreWeight = 0.2;

	public RouteSelectionResult Select(RouteIntent intent, IReadOnlyList<RouteCandidate> candidates, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var retained = new List<RankedRouteCandidate>();
		var excluded = new List<ExcludedRouteCandidate>();
		var ranked = ranker.Rank(intent, candidates);
		foreach (var item in ranked)
		{
			var q = assessor.Assess(item.Candidate.Path, cancellationToken);
			var reasons = new List<string>();
			if (!item.Assessment.TargetsMatched) reasons.Add("targets_not_met");
			if (q.Surface.NonRoadMeters > Math.Max(NonRoadMinimumMeters, NonRoadFraction * q.GeometryLengthMeters)) reasons.Add("road_surface_limit_exceeded");
			if (q.Ways.StepsMeters > 0 || q.Ways.FerryMeters > 0 || q.Ways.ConstructionMeters > 0) reasons.Add("road_waytype_excluded");
			var assessment = item.Assessment with { Quality = q, Score = item.Assessment.Score + RepeatScoreWeight * q.RemainingRepeatedMeters / q.GeometryLengthMeters };
			if (reasons.Count > 0)
			{
				excluded.Add(new(item.Candidate.Seed, item.Candidate.Path.DistanceMeters, item.Candidate.Path.EstimatedDurationSeconds, assessment, reasons.ToArray()));
				continue;
			}
			var warnings = item.Warnings.ToList();
			if (q.SurfaceEvidenceState != SurfaceEvidenceState.Complete) warnings.Add("road_surface_unknown");
			if (q.Surface.NonRoadMeters > 0) warnings.Add("road_surface_non_road");
			if (q.Surface.OtherKnownMeters > 0) warnings.Add("road_surface_other");
			if (q.Ways.PathMeters > 0) warnings.Add("road_path_present");
			if (q.Ways.TrackMeters > 0) warnings.Add("road_track_present");
			if (q.Ways.FootwayMeters > 0) warnings.Add("road_footway_present");
			if (q.RemainingRepeatedMeters / q.GeometryLengthMeters > RepeatWarningFraction) warnings.Add("road_retracing");
			retained.Add(item with { Assessment = assessment, Warnings = warnings.ToArray() });
		}
		cancellationToken.ThrowIfCancellationRequested();
		return new(retained.OrderBy(x => x.Assessment.Score).ThenBy(x => x.Candidate.Seed).ToArray(), excluded.ToArray(), ranked.Any(x => x.Assessment.TargetsMatched));
	}

	public static void AddWarnings(RouteSelectionResult result, List<string> warnings)
	{
		if (!result.AnyTargetsMatched) warnings.Add("no_candidate_within_tolerance");
		if (result.Excluded.Count > 0) warnings.Add("candidates_excluded");
		if (result.Retained.Count == 0) warnings.Add("no_candidate_meets_requirements");
	}
}
