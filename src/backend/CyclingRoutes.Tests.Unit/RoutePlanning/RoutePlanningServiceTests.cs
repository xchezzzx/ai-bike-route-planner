using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class RoutePlanningServiceTests
{
	[Theory]
	[InlineData(ElevationPreference.Balanced, 0, 2)]
	[InlineData(ElevationPreference.Minimize, 1, 3)]
	[InlineData(ElevationPreference.SeekClimbs, 1, 3)]
	public async Task MatchingTargetsSkipAdvisorOnlyForBalancedElevation(ElevationPreference elevation, int adviceCalls, int routeCalls)
	{
		var provider = new Provider((seed, _) => Task.FromResult(Loop(seed, 20000)));
		var advisor = new Advisor((_, _) => Task.FromResult(Advice()));
		var result = await Service(provider, advisor).PlanAsync(Intent(elevation), TestContext.Current.CancellationToken);
		Assert.Equal(routeCalls, result.Search.AttemptedCount);
		Assert.Equal(adviceCalls, result.AdvisorCallCount);
		Assert.Equal(adviceCalls, advisor.Calls.Count);
		Assert.All(result.Search.Candidates, c => Assert.True(c.Assessment.TargetsMatched));
		Assert.Equal(adviceCalls == 0 ? RouteAdvisorStatus.NotNeeded : RouteAdvisorStatus.Searched, result.AdvisorStatus);
	}

	[Fact]
	public async Task AdviceChangesOnlyThirdCallAndPreservesTargetsAndPriorCandidates()
	{
		var provider = new Provider((seed, _) => Task.FromResult(Loop(seed, seed == 7 ? 20100 : 28000)));
		var advisor = new Advisor((context, _) =>
		{
			Assert.Equal(20000, context.Preferences.TargetDistanceMeters);
			Assert.Equal(2, context.Observations.Count);
			Assert.All(context.Observations, o => Assert.Equal(8000, o.DistanceDeltaMeters));
			return Task.FromResult(Advice());
		});
		var result = await Service(provider, advisor).PlanAsync(Intent(), TestContext.Current.CancellationToken);
		Assert.Equal(new[] { (1, 20000d), (2, 20000d / 1.4), (7, 16000d) }.Select(x => (x.Item1, Math.Round(x.Item2, 6))), provider.Calls.Select(x => (x.Item1, Math.Round(x.Item2, 6))));
		Assert.Equal(20000, result.Search.RequestedLengthMeters);
		Assert.Equal(new[] { 7 }, result.Search.Candidates.Select(x => x.Seed));
		Assert.Equal(new[] { 1, 2 }, result.Search.ExcludedCandidates.Select(x => x.Seed));
		Assert.Equal(100, result.Search.Candidates[0].Assessment.DistanceDeltaMeters);
		Assert.Equal(new[] { RouteSearchReason.Explore, RouteSearchReason.Explore, RouteSearchReason.Distance }, result.Attempts.Select(x => x.Reason));
		Assert.Equal(2, advisor.Calls[0].Observations.Count);
		Assert.Contains("trkpt", result.Search.Candidates[0].Route.Gpx);
	}

	[Fact]
	public async Task StopNeverExecutesAThirdCall()
	{
		var provider = new Provider((seed, _) => Task.FromResult(Loop(seed)));
		var result = await Service(provider, new Advisor((_, _) => Task.FromResult(new RouteSearchAdvice(RouteSearchAction.Stop, null, null, RouteSearchReason.Stop))))
			.PlanAsync(Intent(), TestContext.Current.CancellationToken);
		Assert.Equal(2, result.Search.AttemptedCount);
		Assert.Equal(2, result.Attempts.Count);
		Assert.Equal(RouteAdvisorStatus.Stopped, result.AdvisorStatus);
	}

	[Theory]
	[InlineData(RouteSearchAdvisorFailure.NotConfigured)] [InlineData(RouteSearchAdvisorFailure.Authentication)]
	[InlineData(RouteSearchAdvisorFailure.Quota)] [InlineData(RouteSearchAdvisorFailure.Unavailable)]
	[InlineData(RouteSearchAdvisorFailure.Timeout)] [InlineData(RouteSearchAdvisorFailure.InvalidResponse)]
	public async Task AdvisorFailureFallsBackWithoutRetry(RouteSearchAdvisorFailure failure)
	{
		var provider = new Provider((seed, _) => Task.FromResult(Loop(seed)));
		var advisor = new Advisor((_, _) => throw new RouteSearchAdvisorException(failure));
		var result = await Service(provider, advisor).PlanAsync(Intent(), TestContext.Current.CancellationToken);
		Assert.Equal(new[] { 1, 2, 3 }, provider.Calls.Select(x => x.Item1));
		Assert.Equal(20000d / 1.4, provider.Calls[1].Item2, 6);
		Assert.Equal(20000d / 1.4 / 1.4, provider.Calls[2].Item2, 6);
		Assert.Single(advisor.Calls);
		Assert.Equal(RouteAdvisorStatus.Failed, result.AdvisorStatus);
		Assert.Equal(failure, result.AdvisorFailure);
		Assert.Contains("advisor_fallback", result.Search.Warnings);
	}

	[Fact]
	public async Task InvalidProposalIsRejectedNotClamped()
	{
		var provider = new Provider((seed, _) => Task.FromResult(Loop(seed)));
		var result = await Service(provider, new Advisor((_, _) => Task.FromResult(Advice() with { RequestedLengthMeters = 999 })))
			.PlanAsync(Intent(), TestContext.Current.CancellationToken);
		Assert.Equal(3, provider.Calls[^1].Item1);
		Assert.Equal(20000d / 1.4 / 1.4, provider.Calls[^1].Item2, 6);
		Assert.Equal(RouteSearchAdvisorFailure.InvalidResponse, result.AdvisorFailure);
	}

	[Theory]
	[InlineData(false)] [InlineData(true)]
	public async Task NoInitialCandidatesSkipAdvisorAndConsumeNoRouteAttempts(bool allNoRoute)
	{
		var provider = new Provider((seed, _) => seed == 3 && !allNoRoute ? Task.FromResult(Loop(seed)) : throw new RoutingException(RoutingFailure.NoRoute));
		var advisor = new Advisor((_, _) => throw new InvalidOperationException("Must not call advisor"));
		if (allNoRoute)
			Assert.Equal(RoutingFailure.NoRoute, (await Assert.ThrowsAsync<RoutingException>(() => Service(provider, advisor).PlanAsync(Intent(), TestContext.Current.CancellationToken))).Failure);
		else
		{
			var result = await Service(provider, advisor).PlanAsync(Intent(), TestContext.Current.CancellationToken);
			Assert.Equal(RouteAdvisorStatus.SkippedNoCandidates, result.AdvisorStatus);
			Assert.Equal(new[] { RouteSearchOutcome.NoRoute, RouteSearchOutcome.NoRoute, RouteSearchOutcome.Accepted }, result.Attempts.Select(x => x.Outcome));
			Assert.Empty(result.Search.Candidates);
			Assert.Equal(3, Assert.Single(result.Search.ExcludedCandidates).Seed);
		}
		Assert.Equal(3, provider.Calls.Count);
		Assert.Empty(advisor.Calls);
	}

	[Theory]
	[InlineData(false)] [InlineData(true)]
	public async Task DuplicateThirdGeometryCannotReplacePriorRoute(bool reverse)
	{
		var path = Loop(1);
		var duplicate = path with { Points = (reverse ? path.Points.Reverse() : path.Points).Select(p => p with { ElevationMeters = 999 }).ToArray() };
		var result = await Service(new Provider((seed, _) => Task.FromResult(seed == 7 ? duplicate : Loop(seed))), new Advisor((_, _) => Task.FromResult(Advice())))
			.PlanAsync(Intent(), TestContext.Current.CancellationToken);
		Assert.Empty(result.Search.Candidates);
		Assert.Equal(new[] { 1, 2 }, result.Search.ExcludedCandidates.Select(x => x.Seed));
		Assert.Equal(RouteSearchOutcome.Duplicate, result.Attempts[^1].Outcome);
		Assert.Equal(1, path.Points[0].ElevationMeters);
	}

	[Theory]
	[InlineData(RoutingFailure.Unavailable)] [InlineData(RoutingFailure.Timeout)] [InlineData(RoutingFailure.RateLimited)]
	[InlineData(RoutingFailure.InvalidResponse)] [InlineData(RoutingFailure.NotConfigured)]
	[InlineData(RoutingFailure.CredentialsRejected)] [InlineData(RoutingFailure.LimitExceeded)]
	public async Task RoutingFailuresStopCallsAndPreservePartialResult(RoutingFailure failure)
	{
		var provider = new Provider((seed, _) => seed == 1 ? Task.FromResult(Loop(seed)) : throw new RoutingException(failure));
		var advisor = new Advisor((_, _) => throw new InvalidOperationException());
		var result = await Service(provider, advisor).PlanAsync(Intent(), TestContext.Current.CancellationToken);
		Assert.Equal(failure, result.Search.IncompleteFailure);
		Assert.Equal(RouteAdvisorStatus.SkippedRoutingFailure, result.AdvisorStatus);
		Assert.Equal(RouteSearchOutcome.Failed, result.Attempts[1].Outcome);
		Assert.Equal(failure, result.Attempts[1].Failure);
		Assert.Empty(result.Search.Candidates);
		Assert.Equal(1, Assert.Single(result.Search.ExcludedCandidates).Seed);
		Assert.Equal(2, provider.Calls.Count);
		Assert.Empty(advisor.Calls);
	}

	[Fact]
	public async Task InvalidLoopIsNotRepaired()
	{
		var path = Loop(1) with { Points = [new(new(32, 34), null)] };
		var provider = new Provider((_, _) => Task.FromResult(path));
		Assert.Equal(RoutingFailure.InvalidResponse, (await Assert.ThrowsAsync<RoutingException>(() => Service(provider, new Advisor((_, _) => Task.FromResult(Advice())))
			.PlanAsync(Intent(), TestContext.Current.CancellationToken))).Failure);
		Assert.Single(provider.Calls);
	}

	[Fact]
	public async Task AdvisorThirtySecondDeadlineFallsBack()
	{
		var clock = new PlanningClock();
		var provider = new Provider((seed, _) => Task.FromResult(Loop(seed)));
		var advisor = new Advisor(async (_, ct) => { clock.Advance(29); Assert.False(ct.IsCancellationRequested); clock.Advance(1); await Task.Delay(Timeout.Infinite, ct); return Advice(); });
		var result = await Service(provider, advisor, clock).PlanAsync(Intent(), TestContext.Current.CancellationToken);
		Assert.Equal(RouteSearchAdvisorFailure.Timeout, result.AdvisorFailure);
		Assert.Equal(3, provider.Calls.Count);
	}

	[Fact]
	public async Task OverallDeadlineAfterAdviceStartsNoFurtherRoute()
	{
		var clock = new PlanningClock();
		var provider = new Provider((seed, _) => Task.FromResult(Loop(seed)));
		var advisor = new Advisor((_, _) => { clock.Advance(90); return Task.FromResult(Advice()); });
		var result = await Service(provider, advisor, clock).PlanAsync(Intent(), TestContext.Current.CancellationToken);
		Assert.Equal(RoutingFailure.Timeout, result.Search.IncompleteFailure);
		Assert.Equal(2, provider.Calls.Count);
		Assert.Equal(2, result.Attempts.Count);
		Assert.All(clock.Timers, t => Assert.True(t.Disposed));
	}

	[Theory]
	[InlineData("before")] [InlineData("route")] [InlineData("advisor")]
	[InlineData("advisorFailure")] [InlineData("deadline")] [InlineData("third")]
	public async Task CallerCancellationAlwaysWins(string stage)
	{
		using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		var clock = new PlanningClock();
		var provider = new Provider((seed, _) => { if (stage == "route" || stage == "third" && seed == 7) caller.Cancel(); return Task.FromResult(Loop(seed)); });
		var advisor = new Advisor((_, _) =>
		{
			if (stage is "advisor" or "advisorFailure" or "deadline") caller.Cancel();
			if (stage == "deadline") clock.Advance(90);
			if (stage == "advisorFailure") throw new RouteSearchAdvisorException(RouteSearchAdvisorFailure.Quota);
			return Task.FromResult(Advice());
		});
		if (stage == "before") caller.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(provider, advisor, clock).PlanAsync(Intent(), caller.Token));
		Assert.Equal(stage == "before" ? 0 : stage == "route" ? 1 : stage == "third" ? 3 : 2, provider.Calls.Count);
	}

	[Fact]
	public async Task DurationOnlyUsesTwentyKilometresPerHourAndOriginalRanking()
	{
		var provider = new Provider((seed, _) => Task.FromResult(Loop(seed) with { EstimatedDurationSeconds = 5000 }));
		var result = await Service(provider, new Advisor((_, _) => Task.FromResult(Advice()))).PlanAsync(
			new(new(32, 34), RouteShape.Loop, CyclingProfile.Road, targetDuration: TimeSpan.FromHours(1)), TestContext.Current.CancellationToken);
		Assert.Equal(20000, result.Search.RequestedLengthMeters);
		Assert.Contains("initial_speed_20_kmh", result.Search.Assumptions);
		Assert.Empty(result.Search.Candidates);
		Assert.Equal(3, result.Search.ExcludedCandidates.Count);
		Assert.All(result.Search.ExcludedCandidates, c => { Assert.Equal(1400, c.Assessment.DurationDeltaSeconds); Assert.Null(c.Assessment.DistanceDeltaMeters); });
	}

	private static RoutePlanningService Service(Provider p, Advisor a, TimeProvider? clock = null) => new(p, a, new(new(), new()), clock ?? TimeProvider.System, RouteNamingFixture.NeutralNames);
	private static RouteIntent Intent(ElevationPreference elevation = ElevationPreference.Balanced) => new(new(32, 34), RouteShape.Loop, CyclingProfile.Road, new(20000), elevation: elevation);
	private static RouteSearchAdvice Advice() => new(RouteSearchAction.Search, 7, 16000, RouteSearchReason.Distance);
	private static RoutedPath Loop(int seed, double distance = 28000) => new(
		[new(new(32, 34), 1), new(new(32 + seed * 0.001, 34.01), 2), new(new(32.01, 34.02), 3), new(new(32, 34), 1)], distance, 3600, 100, 100, "test");
	private sealed class Provider(Func<int, CancellationToken, Task<RoutedPath>> run) : IRoutingProvider
	{
		public List<(int, double)> Calls { get; } = [];
		public Task<RoutedPath> GetRoadLoopAsync(GeoCoordinate start, double requestedLengthMeters, int seed, CancellationToken cancellationToken)
		{ Calls.Add((seed, requestedLengthMeters)); Assert.Equal(new(32, 34), start); return run(seed, cancellationToken); }
		public Task<RoutedPath> GetRoadRouteAsync(GeoCoordinate start, GeoCoordinate destination, CancellationToken cancellationToken) => throw new InvalidOperationException();
	}
	private sealed class Advisor(Func<RouteSearchContext, CancellationToken, Task<RouteSearchAdvice>> run) : IRouteSearchAdvisor
	{
		public List<RouteSearchContext> Calls { get; } = [];
		public Task<RouteSearchAdvice> AdviseAsync(RouteSearchContext context, CancellationToken cancellationToken) { Calls.Add(context); return run(context, cancellationToken); }
	}

	private sealed class PlanningClock : TimeProvider
	{
		public List<ManualTimer> Timers { get; } = [];
		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{ var timer = new ManualTimer(callback, state, dueTime); Timers.Add(timer); return timer; }
		public void Advance(int seconds) { foreach (var timer in Timers.ToArray()) timer.Advance(TimeSpan.FromSeconds(seconds)); }
		public sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
		{
			public bool Disposed { get; private set; }
			private TimeSpan remaining = due;
			public void Advance(TimeSpan elapsed) { if (Disposed || remaining == Timeout.InfiniteTimeSpan) return; remaining -= elapsed; if (remaining > TimeSpan.Zero) return; remaining = Timeout.InfiniteTimeSpan; callback(state); }
			public bool Change(TimeSpan dueTime, TimeSpan period) { remaining = dueTime; return !Disposed; }
			public void Dispose() => Disposed = true;
			public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
		}
	}
}
