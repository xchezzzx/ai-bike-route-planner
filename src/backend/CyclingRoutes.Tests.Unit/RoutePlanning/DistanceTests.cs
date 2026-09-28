using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class DistanceTests
{
	[Theory]
	[InlineData(0.5)]
	[InlineData(1)]
	[InlineData(40000)]
	[InlineData(double.Epsilon)]
	[InlineData(double.MaxValue)]
	public void Constructor_AcceptsPositiveFiniteMeters(double meters)
	{
		var distance = new Distance(meters);

		Assert.Equal(meters, distance.Meters);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(-0.001)]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity)]
	public void Constructor_RejectsInvalidMeters(double meters)
	{
		Assert.Throws<ArgumentOutOfRangeException>("meters", () => new Distance(meters));
	}

	[Fact]
	public void Equality_UsesMeters()
	{
		var distance = new Distance(40000);
		var equalDistance = new Distance(40000);

		Assert.Equal(distance, equalDistance);
		Assert.Equal(distance.GetHashCode(), equalDistance.GetHashCode());
		Assert.NotEqual(distance, new Distance(40001));
	}
}
