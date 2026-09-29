using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class AdaptiveLoopSearchTests
{
	[Theory]
	[InlineData(false, false)] [InlineData(true, false)]
	[InlineData(false, true)] [InlineData(true, true)]
	public async Task RangeMidpointSeedsSearchAndAdvisorReceivesOriginalBounds(bool advised, bool durationOnly)
	{
		var provider = new Provider((length, seed) => Path(seed, length * 1.4, length * 1.4 * 3600 / 20000));
		var intent = new RouteIntent(new(32, 34), RouteShape.Loop, CyclingProfile.Road,
			elevation: ElevationPreference.Minimize,
			targetDistanceRange: durationOnly ? null : new(18000, 22000),
			targetDurationRange: durationOnly ? new(3000, 4200) : null);
		var result = await Search(advised, provider, intent);
		Assert.Equal(20000, result.RequestedLengthMeters);
		Assert.Equal(20000, provider.Lengths[0]);
		Assert.Equal(20000 / 1.4, provider.Lengths[1], 6);
		Assert.Equal(provider.Lengths[1], provider.Lengths[2], 6);
		Assert.Equal(durationOnly, result.Assumptions.Contains("initial_speed_20_kmh"));
		Assert.Equal(new[] { 2, 3 }, result.Candidates.Select(c => c.Seed));
		Assert.All(result.Candidates, c => Assert.True(c.Assessment.TargetsMatched));
	}

	[Theory]
	[InlineData(18000)]
	[InlineData(20000)]
	[InlineData(22000)]
	public void WithinToleranceKeepsRequestedLength(double actual)
	{
		Assert.Equal(15000, LoopSearchLength.Correct(Intent(20000, null), 20000, 15000, Path(1, actual, 3600)));
	}

	[Theory]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(0)]
	[InlineData(-1)]
	public void InvalidObservationDoesNotChangeSearch(double actual)
	{
		Assert.Equal(15000, LoopSearchLength.Correct(Intent(20000, null), 20000, 15000, Path(1, actual, 3600)));
		Assert.Equal(15000, LoopSearchLength.Correct(Intent(null, 3600), 20000, 15000, Path(1, 20000, actual)));
	}

	[Theory]
	[InlineData(double.MaxValue, 10000)]
	[InlineData(double.Epsilon, 30000)]
	public void ExtremeObservationCannotProduceNonFiniteRequest(double actual, double expected)
	{
		Assert.Equal(expected, LoopSearchLength.Correct(Intent(20000, null), 20000, 20000, Path(1, actual, 3600)));
		Assert.Equal(expected, LoopSearchLength.Correct(Intent(20000, 3600), 20000, 20000, Path(1, actual, actual)));
	}

	[Theory]
	[InlineData(false, false, 1.4)]
	[InlineData(true, false, 1.4)]
	[InlineData(false, false, 0.8)]
	[InlineData(true, false, 0.8)]
	[InlineData(false, true, 1.4)]
	[InlineData(true, true, 1.4)]
	public async Task CorrectsObservedErrorWithoutChangingTargetsOrBudget(bool advised, bool durationOnly, double ratio)
	{
		var provider = new Provider((length, seed) => Path(seed, length * ratio,
			durationOnly ? length * ratio * 3600 / 20000 : 3600));
		var intent = Intent(durationOnly ? null : 20000, durationOnly ? 3600 : null);
		var result = await Search(advised, provider, intent);
		Assert.Equal(3, provider.Lengths.Count);
		Assert.Equal(20000, provider.Lengths[0]);
		Assert.Equal(20000 / ratio, provider.Lengths[1], 6);
		Assert.Equal(provider.Lengths[1], provider.Lengths[2], 6);
		Assert.Equal(20000, result.RequestedLengthMeters);
		Assert.Equal(new[] { 2, 3 }, result.Candidates.Select(c => c.Seed));
		Assert.All(result.Candidates, c => Assert.True(c.Assessment.TargetsMatched));
		Assert.Equal(durationOnly ? null : 20000d, intent.TargetDistance?.Meters);
		Assert.Equal(durationOnly ? 3600d : null, intent.TargetDuration?.TotalSeconds);
	}

	[Theory]
	[InlineData(false, 1000, 10, 1000)]
	[InlineData(true, 1000, 10, 1000)]
	[InlineData(false, 100000, 0.1, 100000)]
	[InlineData(true, 100000, 0.1, 100000)]
	[InlineData(false, 20000, 10, 10000)]
	[InlineData(true, 20000, 10, 10000)]
	[InlineData(false, 20000, 0.1, 30000)]
	[InlineData(true, 20000, 0.1, 30000)]
	public async Task CorrectionStaysWithinAbsoluteAndInitialRelativeBounds(bool advised, double target, double ratio, double expected)
	{
		var provider = new Provider((length, seed) => Path(seed, length * ratio, 3600));
		await Search(advised, provider, Intent(target, null));
		Assert.Equal(new[] { target, expected, expected }, provider.Lengths);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task BothTargetsBalanceRelativeErrorsInsteadOfIgnoringDuration(bool advised)
	{
		var provider = new Provider((length, seed) => Path(seed, length, length * 2 * 3600 / 20000));
		var result = await Search(advised, provider, Intent(20000, 3600));
		Assert.Equal(20000 * 2d / 3, provider.Lengths[1], 6);
		Assert.Equal(provider.Lengths[1], provider.Lengths[2], 6);
		Assert.Empty(result.Candidates);
		Assert.All(result.ExcludedCandidates, c => Assert.Contains("targets_not_met", c.Reasons));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task NoRouteKeepsLastCalibratedLength(bool advised)
	{
		var provider = new Provider((length, seed) => seed == 2
			? throw new RoutingException(RoutingFailure.NoRoute) : Path(seed, length * 1.4, 3600));
		var result = await Search(advised, provider, Intent(20000, null));
		Assert.Equal(20000 / 1.4, provider.Lengths[1], 6);
		Assert.Equal(provider.Lengths[1], provider.Lengths[2]);
		Assert.Equal(3, Assert.Single(result.Candidates).Seed);
	}

	private static async Task<RouteCandidateSearchResult> Search(bool advised, Provider provider, RouteIntent intent)
	{
		var selector = new RoadCandidateSelector(new(), new());
		if (!advised) return await new RouteCandidateService(provider, selector, TimeProvider.System)
			.GenerateAsync(intent, TestContext.Current.CancellationToken);
		var advisor = new FailingAdvisor();
		var result = await new RoutePlanningService(provider, advisor, selector, TimeProvider.System)
			.PlanAsync(intent, TestContext.Current.CancellationToken);
		Assert.Equal(1, advisor.Calls);
		Assert.Equal(provider.Lengths, result.Attempts.Select(a => a.RequestedLengthMeters));
		Assert.Equal(provider.Lengths.Take(2), advisor.Context!.Observations.Select(o => o.RequestedLengthMeters));
		if (intent.TargetDistanceRange is { } dr)
		{
			Assert.Equal(dr.Min, advisor.Context.Preferences.TargetDistanceRangeMeters!.Min);
			Assert.Equal(dr.Max, advisor.Context.Preferences.TargetDistanceRangeMeters.Max);
			Assert.Null(advisor.Context.Preferences.TargetDistanceMeters);
			Assert.Equal(6000, advisor.Context.Observations[0].DistanceDeltaMeters);
			Assert.Equal(0, advisor.Context.Observations[1].DistanceDeltaMeters);
		}
		if (intent.TargetDurationRange is { } tr)
		{
			Assert.Equal(tr.Min, advisor.Context.Preferences.TargetDurationRangeSeconds!.Min);
			Assert.Equal(tr.Max, advisor.Context.Preferences.TargetDurationRangeSeconds.Max);
			Assert.Null(advisor.Context.Preferences.TargetDurationSeconds);
			Assert.Equal(840, advisor.Context.Observations[0].DurationDeltaSeconds);
			Assert.Equal(0, advisor.Context.Observations[1].DurationDeltaSeconds);
		}
		return result.Search;
	}

	private static RouteIntent Intent(double? distance, double? seconds) => new(new(32, 34),
		RouteShape.Loop, CyclingProfile.Road, distance is { } d ? new Distance(d) : null,
		seconds is { } s ? TimeSpan.FromSeconds(s) : null, elevation: ElevationPreference.Minimize);
	private static RoutedPath Path(int seed, double distance, double duration) => new(
		[new(new(32, 34), 1), new(new(32 + seed * 0.001, 34.01), 2), new(new(32.01, 34.02), 3), new(new(32, 34), 1)],
		distance, duration, 100, 100, "test");
	private sealed class Provider(Func<double, int, RoutedPath> run) : IRoutingProvider
	{
		public List<double> Lengths { get; } = [];
		public Task<RoutedPath> GetRoadLoopAsync(GeoCoordinate start, double requestedLengthMeters, int seed, CancellationToken ct)
		{ Lengths.Add(requestedLengthMeters); return Task.FromResult(run(requestedLengthMeters, seed)); }
		public Task<RoutedPath> GetRoadRouteAsync(GeoCoordinate start, GeoCoordinate destination, CancellationToken ct) => throw new InvalidOperationException();
	}
	private sealed class FailingAdvisor : IRouteSearchAdvisor
	{
		public int Calls { get; private set; }
		public RouteSearchContext? Context { get; private set; }
		public Task<RouteSearchAdvice> AdviseAsync(RouteSearchContext context, CancellationToken ct)
		{ Calls++; Context = context; throw new RouteSearchAdvisorException(RouteSearchAdvisorFailure.Unavailable); }
	}
}
