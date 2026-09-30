using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

public sealed class RouteCandidateService(IRoutingProvider provider, RoadCandidateSelector selector, TimeProvider timeProvider)
{
	public async Task<RouteCandidateSearchResult> GenerateAsync(RouteIntent intent, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (intent.Shape != RouteShape.Loop || intent.Profile != CyclingProfile.Road)
			throw new RoutingException(RoutingFailure.UnsupportedIntent);

		var length = RouteTargets.InitialLength(intent);
		if (length is < 1000 or > 100000)
			throw new RoutingException(RoutingFailure.SearchDistanceOutOfRange);

		using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45), timeProvider);
		using var search = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
		var candidates = new List<RouteCandidate>();
		var attemptedCount = 0;
		RoutingFailure? incompleteFailure = null;
		var nextLength = length;

		for (var seed = 1; seed <= 3; seed++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				search.Token.ThrowIfCancellationRequested();
				attemptedCount++;
				var path = await provider.GetRoadLoopAsync(intent.Start, nextLength, seed, search.Token);
				search.Token.ThrowIfCancellationRequested();
				RoadLoopGeometry.Validate(path);
				nextLength = LoopSearchLength.Correct(intent, length, nextLength, path);
				if (!candidates.Any(x => RoadLoopGeometry.SameGeometry(x.Path, path))) candidates.Add(new(seed, path));
			}
			catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
			{
				incompleteFailure = RoutingFailure.Timeout;
				break;
			}
			catch (RoutingException error)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (deadline.IsCancellationRequested) incompleteFailure = RoutingFailure.Timeout;
				else if (error.Failure == RoutingFailure.NoRoute) continue;
				else incompleteFailure = error.Failure;
				break;
			}
		}

		cancellationToken.ThrowIfCancellationRequested();
		if (deadline.IsCancellationRequested) incompleteFailure = RoutingFailure.Timeout;
		if (candidates.Count == 0) throw new RoutingException(incompleteFailure ?? RoutingFailure.NoRoute);

		var selection = selector.Select(intent, candidates, cancellationToken);
		var generated = new List<GeneratedRouteCandidate>();
		foreach (var item in selection.Retained)
		{
			cancellationToken.ThrowIfCancellationRequested();
			generated.Add(new(item.Candidate.Seed, item.Assessment,
				new(item.Candidate.Path, GpxWriter.Write(item.Candidate.Path), item.Warnings)));
		}
		cancellationToken.ThrowIfCancellationRequested();
		if (deadline.IsCancellationRequested) incompleteFailure = RoutingFailure.Timeout;
		var warnings = new List<string> { "candidate_search_limited" };
		if (incompleteFailure is not null) warnings.Add("candidate_generation_incomplete");
		RoadCandidateSelector.AddWarnings(selection, warnings);
		return new(length, RouteTargets.DistanceAim(intent) is null ? ["initial_speed_20_kmh"] : [], attemptedCount,
			warnings.ToArray(), generated.ToArray(), incompleteFailure, selection.Excluded);
	}

}
