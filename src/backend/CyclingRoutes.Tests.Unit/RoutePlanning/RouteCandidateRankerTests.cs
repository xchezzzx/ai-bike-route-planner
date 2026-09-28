using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class RouteCandidateRankerTests
{
	[Theory]
	[InlineData(ElevationPreference.Balanced, 2, 1)]
	[InlineData(ElevationPreference.Minimize, 1, 2)]
	[InlineData(ElevationPreference.SeekClimbs, 2, 1)]
	public void Rank_UsesApprovedDistanceTimeAscentExample(ElevationPreference elevation, int first, int second)
	{
		var intent = Intent(20000, 3600, elevation);
		var result = new RouteCandidateRanker().Rank(intent, [Candidate(1, 20000, 3960, 100), Candidate(2, 21000, 3600, 300)]);
		Assert.Equal(new[] { first, second }, result.Select(x => x.Candidate.Seed));
		Assert.All(result, x => Assert.True(x.Assessment.TargetsMatched));
		Assert.All(result, x => Assert.Empty(x.Warnings));
		if (elevation == ElevationPreference.Minimize)
		{
			Assert.Equal(0.1066666667, result[0].Assessment.Score, 9);
			Assert.Equal(0.22, result[1].Assessment.Score, 9);
		}
	}

	[Theory]
	[InlineData(18000, 20000, true, -2000)]
	[InlineData(22000, 20000, true, 2000)]
	[InlineData(22000.01, 20000, false, 2000.01)]
	[InlineData(17999.99, 20000, false, -2000.01)]
	[InlineData(11002.2, 10002, true, 1000.2)]
	[InlineData(9001.8, 10002, true, -1000.2)]
	[InlineData(11002.20000001, 10002, false, 1000.20000001)]
	[InlineData(9001.79999999, 10002, false, -1000.20000001)]
	public void DistanceTolerance_IsInclusive(double actual, double target, bool matched, double delta)
	{
		var result = Assert.Single(new RouteCandidateRanker().Rank(Intent(target, null), [Candidate(1, actual)]));
		Assert.Equal(matched, result.Assessment.TargetsMatched);
		Assert.Equal(delta, result.Assessment.DistanceDeltaMeters!.Value, 6);
		Assert.Null(result.Assessment.DurationDeltaSeconds);
		Assert.Equal(!matched, result.Warnings.Contains("targets_not_met"));
	}

	[Theory]
	[InlineData(3240, true, -360)]
	[InlineData(3960, true, 360)]
	[InlineData(3960.01, false, 360.01)]
	public void TimeOnly_UsesProviderDuration(double actual, bool matched, double delta)
	{
		var result = Assert.Single(new RouteCandidateRanker().Rank(Intent(null, 3600), [Candidate(1, 50000, actual)]));
		Assert.Equal(matched, result.Assessment.TargetsMatched);
		Assert.Equal(delta, result.Assessment.DurationDeltaSeconds!.Value, 6);
		Assert.Null(result.Assessment.DistanceDeltaMeters);
	}

	[Theory]
	[InlineData(11002.2, true)]
	[InlineData(9001.8, true)]
	[InlineData(11002.20000001, false)]
	[InlineData(9001.79999999, false)]
	public void TimeTolerance_HandlesDecimalBoundaries(double actual, bool matched)
	{
		var result = Assert.Single(new RouteCandidateRanker().Rank(Intent(null, 10002), [Candidate(1, seconds: actual)]));
		Assert.Equal(matched, result.Assessment.TargetsMatched);
		Assert.Equal(!matched, result.Warnings.Contains("targets_not_met"));
		Assert.Equal(actual - 10002, result.Assessment.DurationDeltaSeconds);
	}

	[Fact]
	public void BothTargets_RequireEachToMatchAndAverageTheirErrors()
	{
		var result = Assert.Single(new RouteCandidateRanker().Rank(Intent(20000, 3600), [Candidate(1, 20000, 4320)]));
		Assert.False(result.Assessment.TargetsMatched);
		Assert.Equal(0.1, result.Assessment.Score, 10);
	}

	[Theory]
	[InlineData(ElevationPreference.Minimize)]
	[InlineData(ElevationPreference.SeekClimbs)]
	public void MissingAscent_IsNotZero(ElevationPreference elevation)
	{
		var result = new RouteCandidateRanker().Rank(Intent(20000, null, elevation), [Candidate(1, ascent: null), Candidate(2, ascent: 0)]);
		Assert.Equal(new[] { 2, 1 }, result.Select(x => x.Candidate.Seed));
		Assert.Equal(0, result[0].Assessment.Score);
		Assert.Empty(result[0].Warnings);
		Assert.Equal(0.2, result[1].Assessment.Score);
		Assert.Contains("elevation_data_unavailable", result[1].Warnings);
	}

	[Fact]
	public void Balanced_IgnoresMissingAscentAndBreaksTiesBySeed()
	{
		var result = new RouteCandidateRanker().Rank(Intent(20000, null), [Candidate(3, ascent: 0), Candidate(1, ascent: null), Candidate(2, ascent: 500)]);
		Assert.Equal(new[] { 1, 2, 3 }, result.Select(x => x.Candidate.Seed));
		Assert.All(result, x => Assert.Empty(x.Warnings));
	}

	[Fact]
	public void MatchedTarget_PrecedesBetterScoreOutsideTolerance()
	{
		var result = new RouteCandidateRanker().Rank(Intent(20000, null, ElevationPreference.Minimize),
			[Candidate(1, 22200, ascent: 0), Candidate(2, 20000, ascent: 100)]);
		Assert.Equal(2, result[0].Candidate.Seed);
		Assert.True(result[0].Assessment.Score > result[1].Assessment.Score);
	}

	[Theory]
	[InlineData(ElevationPreference.Balanced)]
	[InlineData(ElevationPreference.Minimize)]
	[InlineData(ElevationPreference.SeekClimbs)]
	public void ExtremeMetrics_KeepScoresAndDeltasFinite(ElevationPreference elevation)
	{
		var intent = new RouteIntent(new(32, 34), RouteShape.Loop, CyclingProfile.Road,
			new Distance(double.Epsilon), TimeSpan.FromTicks(1), elevation: elevation);
		var result = Assert.Single(new RouteCandidateRanker().Rank(intent, [Candidate(1, double.MaxValue, double.MaxValue, double.MaxValue)]));
		Assert.True(double.IsFinite(result.Assessment.Score));
		Assert.InRange(result.Assessment.Score, 0, 1);
		Assert.True(double.IsFinite(result.Assessment.DistanceDeltaMeters!.Value));
		Assert.True(double.IsFinite(result.Assessment.DurationDeltaSeconds!.Value));
		Assert.False(result.Assessment.TargetsMatched);
	}

	private static RouteIntent Intent(double? distance, double? seconds, ElevationPreference elevation = ElevationPreference.Balanced) =>
		new(new(32, 34), RouteShape.Loop, CyclingProfile.Road, distance is { } d ? new Distance(d) : null,
			seconds is { } s ? TimeSpan.FromSeconds(s) : null, elevation: elevation);

	private static RouteCandidate Candidate(int seed, double distance = 20000, double seconds = 3600, double? ascent = 100) =>
		new(seed, new([], distance, seconds, ascent, null, "test attribution"));
}
