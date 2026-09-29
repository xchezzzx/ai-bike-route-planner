using CyclingRoutes.Domain.RoutePlanning;
using CyclingRoutes.Contracts.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

public enum RouteSearchAction { Stop, Search }
public enum RouteSearchReason { Distance, Duration, Elevation, Explore, Stop }
public enum RouteSearchOutcome { Accepted, Duplicate, NoRoute, Failed }

public sealed record RouteSearchPreferences(RouteShape Shape, CyclingProfile Profile,
	ElevationPreference Elevation, double? TargetDistanceMeters, double? TargetDurationSeconds,
	DistanceRangeResponse? TargetDistanceRangeMeters = null, DurationRangeResponse? TargetDurationRangeSeconds = null);

public sealed record RouteSearchObservation(int Seed, double RequestedLengthMeters, RouteSearchOutcome Outcome,
	double? DistanceMeters, double? DurationSeconds, double? AscentMeters, double? DistanceDeltaMeters, double? DurationDeltaSeconds);

public sealed record RouteSearchContext(RouteSearchPreferences Preferences, double InitialLengthMeters,
	IReadOnlyList<RouteSearchObservation> Observations);

public sealed record RouteSearchAdvice(RouteSearchAction Action, int? Seed, double? RequestedLengthMeters, RouteSearchReason Reason);
