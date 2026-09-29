using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class RouteIntentTests
{
	private static readonly GeoCoordinate Start = new(32.0853, 34.7818);
	private static readonly GeoCoordinate Destination = new(32.1663, 34.8433);

	[Theory]
	[InlineData(RouteShape.Loop, true, false)]
	[InlineData(RouteShape.Loop, false, true)]
	[InlineData(RouteShape.Loop, true, true)]
	[InlineData(RouteShape.PointToPoint, true, false)]
	[InlineData(RouteShape.PointToPoint, false, true)]
	[InlineData(RouteShape.PointToPoint, true, true)]
	[InlineData(RouteShape.PointToPoint, false, false)]
	public void Constructor_AcceptsEitherOrBothTargets(RouteShape shape, bool hasDistance, bool hasDuration)
	{
		var intent = new RouteIntent(Start, shape, CyclingProfile.Gravel,
			targetDistance: hasDistance ? new Distance(40000) : null,
			targetDuration: hasDuration ? TimeSpan.FromHours(2) : null,
			destination: shape == RouteShape.PointToPoint ? Destination : null);

		Assert.Equal(Start, intent.Start);
		Assert.Equal(shape, intent.Shape);
		Assert.Equal(CyclingProfile.Gravel, intent.Profile);
		Assert.Equal(ElevationPreference.Balanced, intent.Elevation);
		Assert.Equal(hasDistance ? 40000d : (double?)null, intent.TargetDistance?.Meters);
		Assert.Equal(hasDuration ? TimeSpan.FromHours(2) : (TimeSpan?)null, intent.TargetDuration);
		Assert.Equal(shape == RouteShape.PointToPoint ? Destination : null, intent.Destination);
	}

	[Theory]
	[InlineData(CyclingProfile.Road, ElevationPreference.Minimize)]
	[InlineData(CyclingProfile.Road, ElevationPreference.Balanced)]
	[InlineData(CyclingProfile.Road, ElevationPreference.SeekClimbs)]
	[InlineData(CyclingProfile.Gravel, ElevationPreference.Minimize)]
	[InlineData(CyclingProfile.Gravel, ElevationPreference.Balanced)]
	[InlineData(CyclingProfile.Gravel, ElevationPreference.SeekClimbs)]
	public void Constructor_PreservesExplicitPreferences(CyclingProfile profile, ElevationPreference elevation)
	{
		var intent = new RouteIntent(Start, RouteShape.Loop, profile,
			targetDistance: new Distance(40000), elevation: elevation);

		Assert.Equal(profile, intent.Profile);
		Assert.Equal(elevation, intent.Elevation);
	}

	[Fact]
	public void Constructor_RejectsNullStart()
	{
		Assert.Throws<ArgumentNullException>("start", () => new RouteIntent(
			null!, RouteShape.Loop, CyclingProfile.Road, targetDistance: new Distance(40000)));
	}

	[Theory]
	[InlineData(RouteShape.Loop)]
	public void Constructor_RejectsMissingTargets(RouteShape shape)
	{
		Assert.Throws<ArgumentException>(() => new RouteIntent(Start, shape, CyclingProfile.Road,
			destination: shape == RouteShape.PointToPoint ? Destination : null));
	}

	[Fact]
	public void Constructor_RejectsLoopWithDestination()
	{
		Assert.Throws<ArgumentException>("destination", () => new RouteIntent(
			Start, RouteShape.Loop, CyclingProfile.Road,
			targetDistance: new Distance(40000), destination: Destination));
	}

	[Fact]
	public void Constructor_RejectsPointToPointWithoutDestination()
	{
		Assert.Throws<ArgumentException>("destination", () => new RouteIntent(
			Start, RouteShape.PointToPoint, CyclingProfile.Road, targetDistance: new Distance(40000)));
	}

	[Fact]
	public void Constructor_RejectsDestinationEqualToStartByValue()
	{
		var sameLocation = new GeoCoordinate(32.0853, 34.7818);

		Assert.NotSame(Start, sameLocation);
		Assert.Throws<ArgumentException>("destination", () => new RouteIntent(
			Start, RouteShape.PointToPoint, CyclingProfile.Road,
			targetDistance: new Distance(40000), destination: sameLocation));
	}

	[Theory]
	[InlineData(0, false)]
	[InlineData(0, true)]
	[InlineData(-1, false)]
	[InlineData(-1, true)]
	[InlineData(long.MinValue, false)]
	[InlineData(long.MinValue, true)]
	public void Constructor_RejectsNonPositiveDurationEvenWithDistance(long ticks, bool hasDistance)
	{
		Assert.Throws<ArgumentOutOfRangeException>("targetDuration", () => new RouteIntent(
			Start, RouteShape.Loop, CyclingProfile.Road,
			targetDistance: hasDistance ? new Distance(40000) : null,
			targetDuration: TimeSpan.FromTicks(ticks)));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(long.MaxValue)]
	public void Constructor_AcceptsPositiveDurationBoundaries(long ticks)
	{
		var intent = new RouteIntent(Start, RouteShape.Loop, CyclingProfile.Road,
			targetDuration: TimeSpan.FromTicks(ticks));

		Assert.Equal(TimeSpan.FromTicks(ticks), intent.TargetDuration);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(99)]
	public void Constructor_RejectsUndefinedShape(int value)
	{
		Assert.Throws<ArgumentOutOfRangeException>("shape", () => new RouteIntent(
			Start, (RouteShape)value, CyclingProfile.Road, targetDistance: new Distance(40000)));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(99)]
	public void Constructor_RejectsUndefinedProfile(int value)
	{
		Assert.Throws<ArgumentOutOfRangeException>("profile", () => new RouteIntent(
			Start, RouteShape.Loop, (CyclingProfile)value, targetDistance: new Distance(40000)));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(99)]
	public void Constructor_RejectsUndefinedElevation(int value)
	{
		Assert.Throws<ArgumentOutOfRangeException>("elevation", () => new RouteIntent(
			Start, RouteShape.Loop, CyclingProfile.Road,
			targetDistance: new Distance(40000), elevation: (ElevationPreference)value));
	}
}
