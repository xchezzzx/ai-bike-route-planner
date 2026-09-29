using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

public static class LoopSearchLength
{
	public static double Correct(RouteIntent intent, double initialLength, double requestedLength, RoutedPath observed)
	{
		var minimumFactor = double.PositiveInfinity;
		var maximumFactor = 0d;
		var matched = true;
		if (intent.TargetDistance is { } distance && !Include(distance.Meters, observed.DistanceMeters)) return requestedLength;
		if (intent.TargetDuration is { } duration && !Include(duration.TotalSeconds, observed.EstimatedDurationSeconds)) return requestedLength;
		if (matched) return requestedLength;

		// For two targets, balance their worst relative error under a local linear estimate.
		var factor = minimumFactor == maximumFactor ? minimumFactor : 2 * minimumFactor / (1 + minimumFactor / maximumFactor);
		return Math.Clamp(requestedLength * factor, Math.Max(1000, initialLength * 0.5), Math.Min(100000, initialLength * 1.5));

		bool Include(double target, double actual)
		{
			if (!double.IsFinite(actual) || actual <= 0) return false;
			var factor = target / actual;
			minimumFactor = Math.Min(minimumFactor, factor);
			maximumFactor = Math.Max(maximumFactor, factor);
			matched &= Math.Abs(actual - target) / target <= 0.10 + 1e-15;
			return true;
		}
	}
}
