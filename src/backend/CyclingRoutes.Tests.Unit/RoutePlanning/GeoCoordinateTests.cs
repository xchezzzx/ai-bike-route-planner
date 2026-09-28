using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class GeoCoordinateTests
{
	[Theory]
	[InlineData(32.0853, 34.7818)]
	[InlineData(0, 0)]
	[InlineData(-90, -180)]
	[InlineData(-90, 180)]
	[InlineData(90, -180)]
	[InlineData(90, 180)]
	public void Constructor_AcceptsValidCoordinates(double latitude, double longitude)
	{
		var coordinate = new GeoCoordinate(latitude, longitude);

		Assert.Equal(latitude, coordinate.Latitude);
		Assert.Equal(longitude, coordinate.Longitude);
	}

	[Theory]
	[InlineData(-90.000001)]
	[InlineData(90.000001)]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity)]
	public void Constructor_RejectsInvalidLatitude(double latitude)
	{
		Assert.Throws<ArgumentOutOfRangeException>("latitude", () => new GeoCoordinate(latitude, 34));
	}

	[Theory]
	[InlineData(-180.000001)]
	[InlineData(180.000001)]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity)]
	public void Constructor_RejectsInvalidLongitude(double longitude)
	{
		Assert.Throws<ArgumentOutOfRangeException>("longitude", () => new GeoCoordinate(32, longitude));
	}

	[Fact]
	public void Equality_UsesBothCoordinateValues()
	{
		var coordinate = new GeoCoordinate(32, 34);
		var sameLocation = new GeoCoordinate(32, 34);

		Assert.Equal(coordinate, sameLocation);
		Assert.Equal(coordinate.GetHashCode(), sameLocation.GetHashCode());
		Assert.NotEqual(coordinate, new GeoCoordinate(33, 34));
		Assert.NotEqual(coordinate, new GeoCoordinate(32, 35));
	}
}
