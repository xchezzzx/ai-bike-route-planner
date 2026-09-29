using CyclingRoutes.Application.RoutePlanning;
using CyclingRoutes.Contracts.RoutePlanning;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class RouteIntentValidatorTests
{
	private readonly RouteIntentValidator _validator = new();
	private static RouteIntentRequest ValidRequest() => new()
	{
		Start = new() { Latitude = 32.0853, Longitude = 34.7818 },
		Shape = "loop",
		Profile = "road",
		TargetDistanceMeters = 40000
	};

	[Theory]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity)]
	public void NonFiniteDistance_IsRejected(double value)
	{
		var result = _validator.Validate(ValidRequest() with { TargetDistanceMeters = value }, TestContext.Current.CancellationToken);
		Assert.Null(result.Intent);
		Assert.Equal(["out_of_range"], result.Errors["targetDistanceMeters"]);
	}

	[Theory]
	[InlineData(double.NaN, 0, "start.latitude")]
	[InlineData(double.PositiveInfinity, 0, "start.latitude")]
	[InlineData(double.NegativeInfinity, 0, "start.latitude")]
	[InlineData(0, double.NaN, "start.longitude")]
	[InlineData(0, double.PositiveInfinity, "start.longitude")]
	[InlineData(0, double.NegativeInfinity, "start.longitude")]
	[InlineData(-90.01, 0, "start.latitude")]
	[InlineData(0, 180.01, "start.longitude")]
	[InlineData(0, -180.01, "start.longitude")]
	public void InvalidCoordinate_IsRejected(double latitude, double longitude, string field)
	{
		var result = _validator.Validate(ValidRequest() with { Start = new() { Latitude = latitude, Longitude = longitude } }, TestContext.Current.CancellationToken);
		Assert.Null(result.Intent);
		Assert.Equal(["out_of_range"], result.Errors[field]);
	}

	[Theory]
	[InlineData(-90, -180)]
	[InlineData(90, 180)]
	[InlineData(0, 0)]
	public void ValidCoordinateBoundaries_ArePreserved(double latitude, double longitude)
	{
		var result = _validator.Validate(ValidRequest() with { Start = new() { Latitude = latitude, Longitude = longitude } }, TestContext.Current.CancellationToken);
		Assert.NotNull(result.Intent);
		Assert.Empty(result.Errors);
		Assert.Equal(latitude, result.Intent.Start.Latitude);
		Assert.Equal(longitude, result.Intent.Start.Longitude);
	}

	[Theory]
	[InlineData(1, 10000000)]
	[InlineData(922337203685, 9223372036850000000)]
	public void WholeSecondDurations_AreMappedWithoutOverflow(long seconds, long ticks)
	{
		var result = _validator.Validate(ValidRequest() with { TargetDurationSeconds = seconds }, TestContext.Current.CancellationToken);
		Assert.NotNull(result.Intent);
		Assert.Equal(ticks, result.Intent.TargetDuration!.Value.Ticks);
		Assert.Equal(40000, result.Intent.TargetDistance!.Meters);
	}

	[Theory]
	[InlineData(0, "must_be_positive")]
	[InlineData(-1, "must_be_positive")]
	[InlineData(long.MinValue, "must_be_positive")]
	[InlineData(922337203686, "out_of_range")]
	[InlineData(long.MaxValue, "out_of_range")]
	public void InvalidDurations_ReturnErrorsInsteadOfThrowing(long seconds, string code)
	{
		var result = _validator.Validate(ValidRequest() with { TargetDurationSeconds = seconds }, TestContext.Current.CancellationToken);
		Assert.Null(result.Intent);
		Assert.Equal([code], result.Errors["targetDurationSeconds"]);
	}

	[Fact]
	public void MissingFields_ReturnAllRequiredErrors()
	{
		var result = _validator.Validate(new RouteIntentRequest(), TestContext.Current.CancellationToken);
		Assert.Null(result.Intent);
		Assert.Equal(5, result.Errors.Count);
		Assert.Equal(["required"], result.Errors["start"]);
		Assert.Equal(["required"], result.Errors["shape"]);
		Assert.Equal(["required"], result.Errors["profile"]);
		Assert.Equal(["target_required"], result.Errors["targetDistanceMeters"]);
		Assert.Equal(["target_required"], result.Errors["targetDurationSeconds"]);
	}

	[Fact]
	public void PointToPointWithoutTargets_IsValid()
	{
		var result = _validator.Validate(ValidRequest() with
		{
			Shape = "pointToPoint", Destination = new() { Latitude = 32.1, Longitude = 34.9 },
			TargetDistanceMeters = null
		}, TestContext.Current.CancellationToken);
		Assert.Empty(result.Errors);
		Assert.NotNull(result.Intent);
		Assert.Null(result.Intent.TargetDistance);
		Assert.Null(result.Intent.TargetDuration);
	}

	[Fact]
	public void EqualPointToPointCoordinates_AreRejectedByValue()
	{
		var result = _validator.Validate(ValidRequest() with
		{
			Shape = "pointToPoint",
			Destination = new() { Latitude = 32.0853, Longitude = 34.7818 }
		}, TestContext.Current.CancellationToken);
		Assert.Null(result.Intent);
		Assert.Equal(["must_differ_from_start"], result.Errors["destination"]);
	}

	[Fact]
	public void IncompleteDestination_IsNotSilentlyDiscarded()
	{
		var result = _validator.Validate(ValidRequest() with { Shape = "pointToPoint", Destination = new() { Latitude = 32.1 } }, TestContext.Current.CancellationToken);
		Assert.Null(result.Intent);
		Assert.Equal(["required"], result.Errors["destination.longitude"]);
	}

	[Fact]
	public void ValidRequest_MapsTransportTokensToDomainEnums()
	{
		var result = _validator.Validate(ValidRequest() with { Profile = "gravel", Elevation = "seekClimbs" }, TestContext.Current.CancellationToken);
		Assert.NotNull(result.Intent);
		Assert.Equal(CyclingProfile.Gravel, result.Intent.Profile);
		Assert.Equal(RouteShape.Loop, result.Intent.Shape);
		Assert.Equal(ElevationPreference.SeekClimbs, result.Intent.Elevation);
	}

	[Fact]
	public void CancelledRequest_ThrowsCancellationInsteadOfValidationErrors()
	{
		using var source = new CancellationTokenSource();
		source.Cancel();
		Assert.Throws<OperationCanceledException>(() => _validator.Validate(new RouteIntentRequest(), source.Token));
	}
}
