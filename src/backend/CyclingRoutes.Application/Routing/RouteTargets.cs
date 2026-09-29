using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

internal static class RouteTargets
{
	public static double? DistanceAim(RouteIntent intent) => intent.TargetDistance?.Meters ?? intent.TargetDistanceRange?.Midpoint;
	public static double? DurationAim(RouteIntent intent) => intent.TargetDuration?.TotalSeconds ?? intent.TargetDurationRange?.Midpoint;
	public static double InitialLength(RouteIntent intent) => DistanceAim(intent) ?? DurationAim(intent)!.Value * 20000 / 3600;
	public static double? DistanceDelta(RouteIntent intent, double actual) => intent.TargetDistanceRange?.Delta(actual)
		?? (intent.TargetDistance is { } distance ? actual - distance.Meters : null);
	public static double? DurationDelta(RouteIntent intent, double actual) => intent.TargetDurationRange?.Delta(actual)
		?? (intent.TargetDuration is { } duration ? actual - duration.TotalSeconds : null);
}
