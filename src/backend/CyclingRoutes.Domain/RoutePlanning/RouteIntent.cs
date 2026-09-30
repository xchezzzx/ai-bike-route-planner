namespace CyclingRoutes.Domain.RoutePlanning;

public sealed class RouteIntent
{
	public GeoCoordinate Start { get; }
	public GeoCoordinate? Destination { get; }
	public RouteShape Shape { get; }
	public CyclingProfile Profile { get; }
	public Distance? TargetDistance { get; }
	public TimeSpan? TargetDuration { get; }
	public DistanceRange? TargetDistanceRange { get; }
	public DurationRange? TargetDurationRange { get; }
	public ElevationPreference Elevation { get; }

	public RouteIntent(
		GeoCoordinate start,
		RouteShape shape,
		CyclingProfile profile,
		Distance? targetDistance = null,
		TimeSpan? targetDuration = null,
		GeoCoordinate? destination = null,
		ElevationPreference elevation = ElevationPreference.Balanced,
		DistanceRange? targetDistanceRange = null,
		DurationRange? targetDurationRange = null)
	{
		ArgumentNullException.ThrowIfNull(start);

		if (!Enum.IsDefined(shape))
		{
			throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown route shape.");
		}

		if (!Enum.IsDefined(profile))
		{
			throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown cycling profile.");
		}

		if (!Enum.IsDefined(elevation))
		{
			throw new ArgumentOutOfRangeException(nameof(elevation), elevation, "Unknown elevation preference.");
		}

		if (targetDuration.HasValue && targetDuration.Value <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(targetDuration), targetDuration,
				"Target duration must be greater than zero.");
		}

		if (targetDistance is not null && targetDistanceRange is not null)
			throw new ArgumentException("Distance scalar and range are mutually exclusive.", nameof(targetDistanceRange));
		if (targetDuration is not null && targetDurationRange is not null)
			throw new ArgumentException("Duration scalar and range are mutually exclusive.", nameof(targetDurationRange));

		if (shape == RouteShape.Loop && targetDistance is null && targetDuration is null
			&& targetDistanceRange is null && targetDurationRange is null)
		{
			throw new ArgumentException("A loop requires a target distance or duration.");
		}

		if (shape == RouteShape.Loop && destination is not null)
		{
			throw new ArgumentException("A loop must not specify a destination.", nameof(destination));
		}

		if (shape == RouteShape.PointToPoint && (destination is null || destination == start))
		{
			throw new ArgumentException("A point-to-point route requires a destination different from its start.",
				nameof(destination));
		}

		Start = start;
		Destination = destination;
		Shape = shape;
		Profile = profile;
		TargetDistance = targetDistance;
		TargetDuration = targetDuration;
		TargetDistanceRange = targetDistanceRange;
		TargetDurationRange = targetDurationRange;
		Elevation = elevation;
	}
}
