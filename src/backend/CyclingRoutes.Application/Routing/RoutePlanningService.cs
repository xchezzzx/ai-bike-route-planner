using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

public sealed class RoutePlanningService(IRoutingProvider provider, IRouteSearchAdvisor advisor,
	RoadCandidateSelector selector, TimeProvider timeProvider, RouteNameResolver names)
{
	public async Task<RoutePlanningResult> PlanAsync(RouteIntent intent, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (intent.Shape != RouteShape.Loop || intent.Profile != CyclingProfile.Road)
			throw new RoutingException(RoutingFailure.UnsupportedIntent);
		var initial = RouteTargets.InitialLength(intent);
		if (!double.IsFinite(initial) || initial is < 1000 or > 100000)
			throw new RoutingException(RoutingFailure.SearchDistanceOutOfRange);

		using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90), timeProvider);
		using var search = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
		var candidates = new List<RouteCandidate>();
		var attempts = new List<RoutePlanningAttempt>();
		var observations = new List<RouteSearchObservation>();
		RoutingFailure? incomplete = null;
		RouteSearchAdvisorFailure? advisorFailure = null;
		var advisorStatus = RouteAdvisorStatus.NotNeeded;
		var advisorCalls = 0;
		var nextLength = initial;

		bool CanContinue()
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (deadline.IsCancellationRequested) incomplete = RoutingFailure.Timeout;
			return incomplete is null;
		}

		async Task SearchAsync(int seed, double length, RouteSearchReason reason)
		{
			if (!CanContinue()) return;
			using var callDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15), timeProvider);
			using var call = CancellationTokenSource.CreateLinkedTokenSource(search.Token, callDeadline.Token);
			call.Token.ThrowIfCancellationRequested();
			var index = attempts.Count;
			attempts.Add(new(seed, length, RouteSearchOutcome.Failed, reason, null));
			try
			{
				var path = await provider.GetRoadLoopAsync(intent.Start, length, seed, call.Token);
				call.Token.ThrowIfCancellationRequested();
				RoadLoopGeometry.Validate(path);
				nextLength = LoopSearchLength.Correct(intent, initial, length, path);
				var outcome = candidates.Any(x => RoadLoopGeometry.SameGeometry(x.Path, path))
					? RouteSearchOutcome.Duplicate : RouteSearchOutcome.Accepted;
				if (outcome == RouteSearchOutcome.Accepted) candidates.Add(new(seed, path));
				attempts[index] = new(seed, length, outcome, reason, null);
				observations.Add(new(seed, length, outcome, path.DistanceMeters, path.EstimatedDurationSeconds, path.AscentMeters,
					RouteTargets.DistanceDelta(intent, path.DistanceMeters),
					RouteTargets.DurationDelta(intent, path.EstimatedDurationSeconds)));
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && (deadline.IsCancellationRequested || callDeadline.IsCancellationRequested))
			{
				incomplete = RoutingFailure.Timeout;
				attempts[index] = new(seed, length, RouteSearchOutcome.Failed, reason, incomplete);
			}
			catch (RoutingException error)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var failure = deadline.IsCancellationRequested || callDeadline.IsCancellationRequested ? RoutingFailure.Timeout : error.Failure;
				var outcome = failure == RoutingFailure.NoRoute ? RouteSearchOutcome.NoRoute : RouteSearchOutcome.Failed;
				attempts[index] = new(seed, length, outcome, reason, failure);
				observations.Add(new(seed, length, outcome, null, null, null, null, null));
				if (failure != RoutingFailure.NoRoute) incomplete = failure;
			}
			cancellationToken.ThrowIfCancellationRequested();
		}

		await SearchAsync(1, initial, RouteSearchReason.Explore);
		await SearchAsync(2, nextLength, RouteSearchReason.Explore);
		if (!CanContinue()) advisorStatus = RouteAdvisorStatus.SkippedRoutingFailure;
		else if (candidates.Count == 0)
		{
			advisorStatus = RouteAdvisorStatus.SkippedNoCandidates;
			await SearchAsync(3, nextLength, RouteSearchReason.Explore);
		}
		else if (intent.Elevation != ElevationPreference.Balanced || selector.Select(intent, candidates, cancellationToken).Retained.Count == 0)
		{
			var context = new RouteSearchContext(new(intent.Shape, intent.Profile, intent.Elevation,
				intent.TargetDistance?.Meters, intent.TargetDuration?.TotalSeconds,
				intent.TargetDistanceRange is { } dr ? new(dr.Min, dr.Max) : null,
				intent.TargetDurationRange is { } tr ? new(tr.Min, tr.Max) : null), initial, Array.AsReadOnly(observations.ToArray()));
			RouteSearchAdvice? advice = null;
			using var adviceDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), timeProvider);
			using var adviceCall = CancellationTokenSource.CreateLinkedTokenSource(search.Token, adviceDeadline.Token);
			try
			{
				adviceCall.Token.ThrowIfCancellationRequested();
				advisorCalls++;
				advice = await advisor.AdviseAsync(context, adviceCall.Token);
				adviceCall.Token.ThrowIfCancellationRequested();
				if (advice is null || !RouteSearchProposalPolicy.IsValid(advice, context))
					throw new RouteSearchAdvisorException(RouteSearchAdvisorFailure.InvalidResponse);
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && (deadline.IsCancellationRequested || adviceDeadline.IsCancellationRequested))
			{
				advisorFailure = RouteSearchAdvisorFailure.Timeout;
			}
			catch (RouteSearchAdvisorException error)
			{
				cancellationToken.ThrowIfCancellationRequested();
				advisorFailure = deadline.IsCancellationRequested || adviceDeadline.IsCancellationRequested ? RouteSearchAdvisorFailure.Timeout : error.Failure;
			}
			cancellationToken.ThrowIfCancellationRequested();
			if (advisorFailure is not null)
			{
				advisorStatus = RouteAdvisorStatus.Failed;
				await SearchAsync(3, nextLength, RouteSearchReason.Explore);
			}
			else if (advice!.Action == RouteSearchAction.Stop) advisorStatus = RouteAdvisorStatus.Stopped;
			else
			{
				advisorStatus = RouteAdvisorStatus.Searched;
				await SearchAsync(advice.Seed!.Value, advice.RequestedLengthMeters!.Value, advice.Reason);
			}
		}

		CanContinue();
		if (candidates.Count == 0) throw new RoutingException(incomplete ?? RoutingFailure.NoRoute);
		var selection = selector.Select(intent, candidates, cancellationToken);
		var generated = new List<GeneratedRouteCandidate>();
		foreach (var item in selection.Retained)
		{
			cancellationToken.ThrowIfCancellationRequested();
			generated.Add(new(item.Candidate.Seed, item.Assessment,
				GeneratedRoute.Create(item.Candidate.Path, intent, item.Warnings, names)));
		}
		CanContinue();
		var warnings = new List<string> { "candidate_search_limited" };
		if (incomplete is not null) warnings.Add("candidate_generation_incomplete");
		if (advisorFailure is not null) warnings.Add("advisor_fallback");
		RoadCandidateSelector.AddWarnings(selection, warnings);
		return new(new(initial, RouteTargets.DistanceAim(intent) is null ? ["initial_speed_20_kmh"] : [], attempts.Count,
			warnings.ToArray(), generated.ToArray(), incomplete, selection.Excluded), advisorCalls, advisorStatus, advisorFailure, attempts.ToArray());
	}
}
