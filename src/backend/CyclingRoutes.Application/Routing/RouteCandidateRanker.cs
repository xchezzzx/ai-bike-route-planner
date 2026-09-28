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
		double? distanceDelta = null, durationDelta = null;
		var errorSum = 0d;
		var targetCount = 0;
		var matched = true;
		if (intent.TargetDistance is { } distance)
		{
			distanceDelta = candidate.Path.DistanceMeters - distance.Meters;
			IncludeTarget(distanceDelta.Value, distance.Meters);
		}
		if (intent.TargetDuration is { } duration)
		{
			durationDelta = candidate.Path.EstimatedDurationSeconds - duration.TotalSeconds;
			IncludeTarget(durationDelta.Value, duration.TotalSeconds);
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

		void IncludeTarget(double delta, double target)
		{
			var absoluteDelta = Math.Abs(delta);
			// Clamp before division so extreme positive metrics cannot overflow the score.
			var relativeError = absoluteDelta >= target ? 1 : absoluteDelta / target;
			errorSum += relativeError;
			// Absorb double roundoff at the inclusive boundary without rounding metrics or scores.
			matched &= relativeError <= TargetTolerance + RelativeRoundingAllowance;
			targetCount++;
		}
	}
}
