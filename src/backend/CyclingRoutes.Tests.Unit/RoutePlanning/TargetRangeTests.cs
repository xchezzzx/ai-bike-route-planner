using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class TargetRangeTests
{
	[Theory]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity)]
	public void NonFiniteActualCannotSilentlyMatchRanges(double actual)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new DistanceRange(18000, 22000).Delta(actual));
		Assert.Throws<ArgumentOutOfRangeException>(() => new DurationRange(3000, 4200).Delta(actual));
	}

	[Theory]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity)]
	[InlineData(0)]
	[InlineData(-1)]
	public void InvalidRangeObservationKeepsSearchLength(double actual)
	{
		var intent = new RouteIntent(new(32, 34), RouteShape.Loop, CyclingProfile.Road,
			targetDistanceRange: new(18000, 22000), targetDurationRange: new(3000, 4200));
		Assert.Equal(15000, LoopSearchLength.Correct(intent, 20000, 15000, Path(actual, 3600)));
		Assert.Equal(15000, LoopSearchLength.Correct(intent, 20000, 15000, Path(20000, actual)));
	}

	[Theory]
	[InlineData(18000, true, 0, 0)]
	[InlineData(21000, true, 0, 0)]
	[InlineData(22000, true, 0, 0)]
	[InlineData(17999, false, -1, 0.00005)]
	[InlineData(22001, false, 1, 0.00005)]
	[InlineData(16000, false, -2000, 0.1)]
	[InlineData(24000, false, 2000, 0.1)]
	public void DistanceRangeHasExactInclusiveBoundsAndMidpointNormalizedDelta(double actual, bool matched, double delta, double score)
	{
		var assessment = Assess(Intent(), actual, 3600);
		Assert.Equal(matched, assessment.TargetsMatched);
		Assert.Equal(delta, assessment.DistanceDeltaMeters);
		Assert.Null(assessment.DurationDeltaSeconds);
		Assert.Equal(score, assessment.Score, 10);
	}

	[Theory]
	[InlineData(3000, true, 0)]
	[InlineData(4200, true, 0)]
	[InlineData(2999.5, false, -0.5)]
	[InlineData(4200.5, false, 0.5)]
	public void DurationRangeUsesExactBoundsAndSignedNearestBoundary(double actual, bool matched, double delta)
	{
		var intent = new RouteIntent(new(32, 34), RouteShape.Loop, CyclingProfile.Road, targetDurationRange: new(3000, 4200));
		var assessment = Assess(intent, 20000, actual);
		Assert.Equal(matched, assessment.TargetsMatched);
		Assert.Equal(delta, assessment.DurationDeltaSeconds);
		Assert.Null(assessment.DistanceDeltaMeters);
		Assert.Equal(Math.Abs(delta) / 3600, assessment.Score, 10);
	}

	[Fact]
	public void EqualBoundsAndAdjacentFloatingPointValuesHaveNoExtraTolerance()
	{
		var intent = new RouteIntent(new(32, 34), RouteShape.Loop, CyclingProfile.Road, targetDistanceRange: new(20000, 20000));
		Assert.True(Assess(intent, 20000, 3600).TargetsMatched);
		Assert.False(Assess(intent, Math.BitIncrement(20000), 3600).TargetsMatched);
		Assert.False(Assess(intent, Math.BitDecrement(20000), 3600).TargetsMatched);
	}

	[Theory]
	[InlineData(22000, 3960, true, 0.05)]
	[InlineData(22001, 3600, false, 0.000025)]
	[InlineData(20000, 4320, false, 0.1)]
	public void MixedRangeAndScalarRequireBothAndPreserveScalarTolerance(double distance, double seconds, bool matched, double score)
	{
		var intent = new RouteIntent(new(32, 34), RouteShape.Loop, CyclingProfile.Road,
			targetDuration: TimeSpan.FromSeconds(3600), targetDistanceRange: new(18000, 22000));
		var assessment = Assess(intent, distance, seconds);
		Assert.Equal(matched, assessment.TargetsMatched);
		Assert.Equal(score, assessment.Score, 10);
	}

	[Theory]
	[InlineData(18000, 3000, 15000)]
	[InlineData(22000, 4200, 15000)]
	[InlineData(25000, 4500, 12000)]
	[InlineData(10000, 1800, 30000)]
	public void CalibrationAimsAtMidpointsButKeepsLengthWhenAllConstraintsMatch(double distance, double seconds, double expected)
	{
		var intent = new RouteIntent(new(32, 34), RouteShape.Loop, CyclingProfile.Road,
			targetDistanceRange: new(18000, 22000), targetDurationRange: new(3000, 4200));
		Assert.Equal(expected, LoopSearchLength.Correct(intent, 20000, 15000, Path(distance, seconds)), 8);
	}

	[Fact]
	public void RangeAndScalarForSameMetricAreRejectedByDomain()
	{
		Assert.Throws<ArgumentException>(() => new RouteIntent(new(32, 34), RouteShape.Loop, CyclingProfile.Road,
			targetDistance: new(20000), targetDistanceRange: new(18000, 22000)));
		Assert.Throws<ArgumentException>(() => new RouteIntent(new(32, 34), RouteShape.Loop, CyclingProfile.Road,
			targetDuration: TimeSpan.FromHours(1), targetDurationRange: new(3000, 4200)));
	}

	[Theory]
	[InlineData(0, 1)] [InlineData(-1, 1)] [InlineData(2, 1)]
	[InlineData(double.NaN, 1)] [InlineData(1, double.PositiveInfinity)]
	public void InvalidDistanceBoundsAreRejectedByDomain(double min, double max) =>
		Assert.Throws<ArgumentOutOfRangeException>(() => new DistanceRange(min, max));

	[Theory]
	[InlineData(0, 1)] [InlineData(-1, 1)] [InlineData(2, 1)]
	[InlineData(1, 922337203686)] [InlineData(922337203686, 922337203686)]
	public void InvalidDurationBoundsAreRejectedByDomain(long min, long max) =>
		Assert.Throws<ArgumentOutOfRangeException>(() => new DurationRange(min, max));

	[Fact]
	public void ExtremeRangeKeepsFiniteMidpointScoreAndDelta()
	{
		var range = new DistanceRange(double.MaxValue / 2, double.MaxValue);
		Assert.True(double.IsFinite(range.Midpoint));
		var intent = new RouteIntent(new(32, 34), RouteShape.Loop, CyclingProfile.Road, targetDistanceRange: range);
		var result = Assess(intent, 1, 3600);
		Assert.True(double.IsFinite(result.Score));
		Assert.True(double.IsFinite(result.DistanceDeltaMeters!.Value));
	}

	private static RouteIntent Intent() => new(new(32, 34), RouteShape.Loop, CyclingProfile.Road, targetDistanceRange: new(18000, 22000));
	private static RouteCandidateAssessment Assess(RouteIntent intent, double distance, double seconds) =>
		Assert.Single(new RouteCandidateRanker().Rank(intent, [new(1, Path(distance, seconds))])).Assessment;
	private static RoutedPath Path(double distance, double seconds) => new([], distance, seconds, 100, 100, "test");
}
