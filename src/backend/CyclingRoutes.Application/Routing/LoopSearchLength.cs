using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

public static class LoopSearchLength
{
	public static double Correct(RouteIntent intent, double initialLength, double requestedLength, RoutedPath observed)
	{
		var distanceAim = RouteTargets.DistanceAim(intent);
		var durationAim = RouteTargets.DurationAim(intent);
		if (distanceAim.HasValue && (!double.IsFinite(observed.DistanceMeters) || observed.DistanceMeters <= 0)
			|| durationAim.HasValue && (!double.IsFinite(observed.EstimatedDurationSeconds) || observed.EstimatedDurationSeconds <= 0))
			return requestedLength;
		var minimumFactor = double.PositiveInfinity;
		var maximumFactor = 0d;
		var matched = true;
		if (distanceAim is { } distance) Include(distance, observed.DistanceMeters,
			intent.TargetDistanceRange is { } dr ? dr.Delta(observed.DistanceMeters) == 0 : null);
		if (durationAim is { } duration) Include(duration, observed.EstimatedDurationSeconds,
			intent.TargetDurationRange is { } tr ? tr.Delta(observed.EstimatedDurationSeconds) == 0 : null);
		if (matched) return requestedLength;

		// For two targets, balance their worst relative error under a local linear estimate.
		var factor = minimumFactor == maximumFactor ? minimumFactor : 2 * minimumFactor / (1 + minimumFactor / maximumFactor);
		return Math.Clamp(requestedLength * factor, Math.Max(1000, initialLength * 0.5), Math.Min(100000, initialLength * 1.5));

		void Include(double target, double actual, bool? rangeMatched)
		{
			var factor = target / actual;
			minimumFactor = Math.Min(minimumFactor, factor);
			maximumFactor = Math.Max(maximumFactor, factor);
			matched &= rangeMatched ?? Math.Abs(actual - target) / target <= 0.10 + 1e-15;
		}
	}
}
