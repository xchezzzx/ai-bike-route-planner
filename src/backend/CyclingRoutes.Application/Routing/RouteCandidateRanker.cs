using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

public sealed class RouteCandidateRanker
{
	private const double TargetTolerance = 0.10;
	private const double RelativeRoundingAllowance = 1e-15;

	public IReadOnlyList<RankedRouteCandidate> Rank(RouteIntent intent, IReadOnlyList<RouteCandidate> candidates)
	{
		var maxAscent = candidates.Select(x => x.Path.AscentMeters ?? 0).DefaultIfEmpty(0).Max();
		return candidates.Select(candidate => Assess(intent, candidate, maxAscent))
			.OrderByDescending(x => x.Assessment.TargetsMatched)
			.ThenBy(x => x.Assessment.Score)
			.ThenBy(x => x.Candidate.Seed)
			.ToArray();
	}

	private static RankedRouteCandidate Assess(RouteIntent intent, RouteCandidate candidate, double maxAscent)
	{
		var distanceDelta = RouteTargets.DistanceDelta(intent, candidate.Path.DistanceMeters);
		var durationDelta = RouteTargets.DurationDelta(intent, candidate.Path.EstimatedDurationSeconds);
		var errorSum = 0d;
		var targetCount = 0;
		var matched = true;
		if (RouteTargets.DistanceAim(intent) is { } distance)
		{
			IncludeTarget(distanceDelta!.Value, distance, intent.TargetDistanceRange is not null);
		}
		if (RouteTargets.DurationAim(intent) is { } duration)
		{
			IncludeTarget(durationDelta!.Value, duration, intent.TargetDurationRange is not null);
		}

		var warnings = new List<string>();
		if (!matched) warnings.Add("targets_not_met");
		var score = errorSum / targetCount;
		if (intent.Elevation != ElevationPreference.Balanced)
		{
			var penalty = 0d;
			if (candidate.Path.AscentMeters is not { } ascent)
			{
				penalty = 1;
				warnings.Add("elevation_data_unavailable");
			}
			else if (maxAscent > 0)
			{
				penalty = ascent / maxAscent;
				if (intent.Elevation == ElevationPreference.SeekClimbs) penalty = 1 - penalty;
			}
			score = 0.8 * score + 0.2 * penalty;
		}
		return new(candidate, new(distanceDelta, durationDelta, matched, score), warnings.ToArray());

		void IncludeTarget(double delta, double target, bool explicitRange)
		{
			var absoluteDelta = Math.Abs(delta);
			// Clamp before division so extreme positive metrics cannot overflow the score.
			var relativeError = absoluteDelta >= target ? 1 : absoluteDelta / target;
			errorSum += relativeError;
			// Absorb double roundoff at the inclusive boundary without rounding metrics or scores.
			matched &= explicitRange ? delta == 0 : relativeError <= TargetTolerance + RelativeRoundingAllowance;
			targetCount++;
		}
	}
}
