using System.Xml.Linq;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class RouteCandidateServiceTests
{
	[Fact]
	public async Task DurationOnly_RequestsThreeTwentyKilometreLoops()
	{
		var active = 0;
		var provider = new ScriptedProvider(async (seed, ct) =>
		{
			Assert.Equal(1, Interlocked.Increment(ref active));
			await Task.Yield();
			ct.ThrowIfCancellationRequested();
			Interlocked.Decrement(ref active);
			return Loop(seed) with { EstimatedDurationSeconds = 3900 };
		});
		var result = await Service(provider).GenerateAsync(Intent(null, 3600), TestContext.Current.CancellationToken);
		Assert.Equal(new[] { 1, 2, 3 }, provider.Calls.Select(x => x.Seed));
		Assert.All(provider.Calls, x => { Assert.Equal(20000, x.Length); Assert.Equal(new(32, 34), x.Start); });
		Assert.Equal(3, result.AttemptedCount);
		Assert.Equal(20000, result.RequestedLengthMeters);
		Assert.Equal(new[] { "initial_speed_20_kmh" }, result.Assumptions);
		Assert.Equal(new[] { "candidate_search_limited" }, result.Warnings);
		Assert.Null(result.IncompleteFailure);
		Assert.Equal(3, result.Candidates.Count);
		Assert.All(result.Candidates, x =>
		{
			Assert.Equal(3900, x.Route.Path.EstimatedDurationSeconds);
			Assert.Equal(300, x.Assessment.DurationDeltaSeconds);
			Assert.Null(x.Assessment.DistanceDeltaMeters);
			XNamespace ns = "http://www.topografix.com/GPX/1/1";
			Assert.Equal(4, XDocument.Parse(x.Route.Gpx).Descendants(ns + "trkpt").Count());
		});
	}

	[Theory]
	[InlineData(999d, null)]
	[InlineData(100001d, null)]
	[InlineData(null, 179d)]
	[InlineData(null, 18001d)]
	[InlineData(double.MaxValue, null)]
	public async Task LengthPolicy_RejectsBeforeProvider(double? distance, double? seconds)
	{
		var provider = new ScriptedProvider((seed, _) => Task.FromResult(Loop(seed)));
		var error = await Assert.ThrowsAsync<RoutingException>(() => Service(provider).GenerateAsync(Intent(distance, seconds), TestContext.Current.CancellationToken));
		Assert.Equal(RoutingFailure.SearchDistanceOutOfRange, error.Failure);
		Assert.Empty(provider.Calls);
	}

	[Theory]
	[InlineData(1000d, null, 1000)]
	[InlineData(100000d, null, 100000)]
	[InlineData(null, 180d, 1000)]
	[InlineData(null, 18000d, 100000)]
	[InlineData(20000d, 1d, 20000)]
	public async Task LengthPolicy_AcceptsBoundariesAndPrefersDistance(double? distance, double? seconds, double expected)
	{
		var provider = new ScriptedProvider((seed, _) => Task.FromResult(Loop(seed)));
		var result = await Service(provider).GenerateAsync(Intent(distance, seconds), TestContext.Current.CancellationToken);
		Assert.All(provider.Calls, x => Assert.Equal(expected, x.Length));
		Assert.Equal(distance is null, result.Assumptions.Contains("initial_speed_20_kmh"));
	}

	[Theory]
	[InlineData(CyclingProfile.Gravel, RouteShape.Loop)]
	[InlineData(CyclingProfile.Road, RouteShape.PointToPoint)]
	public async Task UnsupportedIntent_MakesNoCalls(CyclingProfile profile, RouteShape shape)
	{
		var provider = new ScriptedProvider((seed, _) => Task.FromResult(Loop(seed)));
		var intent = new RouteIntent(new(32, 34), shape, profile, new Distance(20000), destination: shape == RouteShape.PointToPoint ? new(33, 35) : null);
		var error = await Assert.ThrowsAsync<RoutingException>(() => Service(provider).GenerateAsync(intent, TestContext.Current.CancellationToken));
		Assert.Equal(RoutingFailure.UnsupportedIntent, error.Failure);
		Assert.Empty(provider.Calls);
	}

	[Theory]
	[InlineData("too-few-points")]
	[InlineData("open")]
	[InlineData("too-few-distinct-positions")]
	public async Task InvalidLoop_StopsWithoutSyntheticClosure(string scenario)
	{
		var path = Loop(1);
		path = path with { Points = scenario switch
		{
			"too-few-points" => [path.Points[0], path.Points[1], path.Points[0]],
			"open" => [path.Points[0], path.Points[1], path.Points[2], new(new(32.02, 34.03), 4)],
			_ => [path.Points[0], path.Points[1], path.Points[0], path.Points[1], path.Points[0]]
		} };
		var originalPoints = path.Points.ToArray();
		var provider = new ScriptedProvider((_, _) => Task.FromResult(path));
		var error = await Assert.ThrowsAsync<RoutingException>(() => Service(provider).GenerateAsync(Intent(), TestContext.Current.CancellationToken));
		Assert.Equal(RoutingFailure.InvalidResponse, error.Failure);
		Assert.Single(provider.Calls);
		Assert.Equal(originalPoints, path.Points);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task DuplicateOrReverse_KeepsFirstSeedIgnoringElevation(bool reverse)
	{
		var original = Loop(1) with { AscentMeters = 100 };
		var duplicate = original with
		{
			Points = (reverse ? original.Points.Reverse() : original.Points).Select(x => x with { ElevationMeters = 999 }).ToArray(),
			AscentMeters = 10000
		};
		var provider = new ScriptedProvider((seed, _) => Task.FromResult(seed switch { 1 => original, 2 => duplicate, _ => Loop(3) with { AscentMeters = 200 } }));
		var result = await Service(provider).GenerateAsync(Intent(elevation: ElevationPreference.Minimize), TestContext.Current.CancellationToken);
		Assert.Equal(3, result.AttemptedCount);
		Assert.Equal(new[] { 1, 3 }, result.Candidates.Select(x => x.Seed));
		Assert.Equal(0.1, result.Candidates[0].Assessment.Score, 10);
		Assert.Equal(100, result.Candidates[0].Route.Path.AscentMeters);
		Assert.Equal(1, result.Candidates[0].Route.Path.Points[0].ElevationMeters);
	}

	[Theory]
	[InlineData(ElevationPreference.Balanced)]
	[InlineData(ElevationPreference.Minimize)]
	[InlineData(ElevationPreference.SeekClimbs)]
	public async Task PartialOverlapAndOutAndBack_AreAllowed(ElevationPreference elevation)
	{
		var original = Loop(1);
		var path = original with { Points = [original.Points[0], original.Points[1], original.Points[2], original.Points[1], original.Points[0] with { ElevationMeters = 3 }] };
		var provider = new ScriptedProvider((seed, _) => Task.FromResult(seed == 1 ? original : path));
		var result = await Service(provider).GenerateAsync(Intent(elevation: elevation), TestContext.Current.CancellationToken);
		Assert.Equal(2, result.Candidates.Count);
	}

	[Fact]
	public async Task NoRoute_ConsumesAttemptAndContinues()
	{
		var provider = new ScriptedProvider((seed, _) => seed == 2 ? Task.FromResult(Loop(2)) : throw new RoutingException(RoutingFailure.NoRoute));
		var result = await Service(provider).GenerateAsync(Intent(), TestContext.Current.CancellationToken);
		Assert.Equal(3, result.AttemptedCount);
		Assert.Equal(2, Assert.Single(result.Candidates).Seed);
		Assert.Null(result.IncompleteFailure);
		Assert.DoesNotContain("candidate_generation_incomplete", result.Warnings);
	}

	[Fact]
	public async Task AllNoRoutes_ThrowsNoRoute()
	{
		var provider = new ScriptedProvider((_, _) => throw new RoutingException(RoutingFailure.NoRoute));
		var error = await Assert.ThrowsAsync<RoutingException>(() => Service(provider).GenerateAsync(Intent(), TestContext.Current.CancellationToken));
		Assert.Equal(RoutingFailure.NoRoute, error.Failure);
		Assert.Equal(3, provider.Calls.Count);
	}

	public static TheoryData<RoutingFailure, bool> StoppingFailures => new(
		from failure in new[] { RoutingFailure.NotConfigured, RoutingFailure.CredentialsRejected, RoutingFailure.RateLimited,
			RoutingFailure.Unavailable, RoutingFailure.Timeout, RoutingFailure.InvalidResponse, RoutingFailure.LimitExceeded }
		from partial in new[] { false, true }
		select (failure, partial));

	[Theory]
	[MemberData(nameof(StoppingFailures))]
	public async Task StoppingFailure_ReturnsPartialOrThrows(RoutingFailure failure, bool partial)
	{
		var path = Loop(1);
		var provider = new ScriptedProvider((seed, _) => seed == 1 && partial ? Task.FromResult(path) : throw new RoutingException(failure));
		if (partial)
		{
			var result = await Service(provider).GenerateAsync(Intent(), TestContext.Current.CancellationToken);
			Assert.Equal(failure, result.IncompleteFailure);
			Assert.Contains("candidate_generation_incomplete", result.Warnings);
			Assert.Equal(2, result.AttemptedCount);
			Assert.Equal(GpxWriter.Write(path), Assert.Single(result.Candidates).Route.Gpx);
		}
		else
		{
			var error = await Assert.ThrowsAsync<RoutingException>(() => Service(provider).GenerateAsync(Intent(), TestContext.Current.CancellationToken));
			Assert.Equal(failure, error.Failure);
		}
		Assert.Equal(partial ? 2 : 1, provider.Calls.Count);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Deadline_ReturnsPartialOrThrowsTimeout(bool partial)
	{
		var clock = new ManualClock();
		var provider = new ScriptedProvider(async (seed, ct) =>
		{
			if (seed == 1 && partial) return Loop(seed);
			clock.Advance(TimeSpan.FromSeconds(44));
			Assert.False(ct.IsCancellationRequested);
			clock.Advance(TimeSpan.FromSeconds(1));
			Assert.True(ct.IsCancellationRequested);
			await Task.Delay(Timeout.Infinite, ct);
			return Loop(seed);
		});
		var service = new RouteCandidateService(provider, new(new(), new()), clock);
		if (partial)
		{
			var result = await service.GenerateAsync(Intent(), TestContext.Current.CancellationToken);
			Assert.Equal(RoutingFailure.Timeout, result.IncompleteFailure);
			Assert.Single(result.Candidates);
		}
		else
		{
			var error = await Assert.ThrowsAsync<RoutingException>(() => service.GenerateAsync(Intent(), TestContext.Current.CancellationToken));
			Assert.Equal(RoutingFailure.Timeout, error.Failure);
		}
		Assert.Equal(partial ? 2 : 1, provider.Calls.Count);
		Assert.True(clock.Disposed);
	}

	[Theory]
	[InlineData("before")]
	[InlineData("failure")]
	[InlineData("deadline")]
	[InlineData("success")]
	public async Task CallerCancellation_AlwaysPropagates(string scenario)
	{
		using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		var clock = new ManualClock();
		var provider = new ScriptedProvider((seed, _) =>
		{
			if (seed == 1) return Task.FromResult(Loop(seed));
			caller.Cancel();
			if (scenario == "deadline") clock.Advance(TimeSpan.FromSeconds(45));
			if (scenario == "failure") throw new RoutingException(RoutingFailure.RateLimited);
			return Task.FromResult(Loop(seed));
		});
		if (scenario == "before") caller.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RouteCandidateService(provider, new(new(), new()), clock).GenerateAsync(Intent(), caller.Token));
		Assert.Equal(scenario == "before" ? 0 : 2, provider.Calls.Count);
	}

	[Fact]
	public async Task NoMatchingCandidate_ReturnsEmptySelectableSet()
	{
		var provider = new ScriptedProvider((seed, _) => Task.FromResult(Loop(seed) with { DistanceMeters = 30000, AscentMeters = null }));
		var result = await Service(provider).GenerateAsync(Intent(elevation: ElevationPreference.Minimize), TestContext.Current.CancellationToken);
		Assert.Contains("no_candidate_within_tolerance", result.Warnings);
		Assert.Empty(result.Candidates);
		Assert.Contains("no_candidate_meets_requirements", result.Warnings);
	}

	private static RouteCandidateService Service(IRoutingProvider provider) => new(provider, new(new(), new()), TimeProvider.System);
	private static RouteIntent Intent(double? distance = 20000, double? seconds = null, ElevationPreference elevation = ElevationPreference.Balanced) =>
		new(new(32, 34), RouteShape.Loop, CyclingProfile.Road, distance is { } d ? new Distance(d) : null,
			seconds is { } s ? TimeSpan.FromSeconds(s) : null, elevation: elevation);
	private static RoutedPath Loop(int seed) => new(
		[new(new(32, 34), 1), new(new(32 + seed * 0.01, 34.01), 2), new(new(32.01, 34.02), 3), new(new(32, 34), 1)],
		20000, 3600, 100, 100, "test attribution");

	private sealed class ScriptedProvider(Func<int, CancellationToken, Task<RoutedPath>> generate) : IRoutingProvider
	{
		public List<(GeoCoordinate Start, double Length, int Seed)> Calls { get; } = [];
		public Task<RoutedPath> GetRoadLoopAsync(GeoCoordinate start, double requestedLengthMeters, int seed, CancellationToken cancellationToken)
		{
			Calls.Add((start, requestedLengthMeters, seed));
			return generate(seed, cancellationToken);
		}
		public Task<RoutedPath> GetRoadRouteAsync(GeoCoordinate start, GeoCoordinate destination, CancellationToken cancellationToken) =>
			throw new Xunit.Sdk.XunitException("A loop search must not call the A-B operation.");
	}

	private sealed class ManualClock : TimeProvider
	{
		private Timer? _timer;
		public bool Disposed => _timer?.Disposed == true;
		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			Assert.Equal(Timeout.InfiniteTimeSpan, period);
			_timer = new(callback, state, dueTime);
			return _timer;
		}
		public void Advance(TimeSpan elapsed) => _timer!.Advance(elapsed);
		private sealed class Timer(TimerCallback callback, object? state, TimeSpan due) : ITimer
		{
			public bool Disposed { get; private set; }
			private TimeSpan _remaining = due;
			public void Advance(TimeSpan elapsed)
			{
				if (Disposed || _remaining == Timeout.InfiniteTimeSpan) return;
				_remaining -= elapsed;
				if (_remaining > TimeSpan.Zero) return;
				_remaining = Timeout.InfiniteTimeSpan;
				callback(state);
			}
			public bool Change(TimeSpan dueTime, TimeSpan period) { _remaining = dueTime; return !Disposed; }
			public void Dispose() => Disposed = true;
			public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
		}
	}
}
