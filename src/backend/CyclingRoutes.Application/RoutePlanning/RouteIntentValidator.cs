using CyclingRoutes.Contracts.RoutePlanning;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.RoutePlanning;

public sealed class RouteIntentValidator
{
	public RouteIntentValidationResult Validate(RouteIntentRequest request, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ArgumentNullException.ThrowIfNull(request);
		var errors = new Dictionary<string, string[]>();
		var start = ReadCoordinate(request.Start, "start", errors);
		var shape = request.Shape switch
		{
			"loop" => RouteShape.Loop,
			"pointToPoint" => RouteShape.PointToPoint,
			_ => (RouteShape?)null
		};
		var profile = request.Profile switch
		{
			"road" => CyclingProfile.Road,
			"gravel" => CyclingProfile.Gravel,
			_ => (CyclingProfile?)null
		};
		var elevation = request.Elevation switch
		{
			null or "balanced" => ElevationPreference.Balanced,
			"minimize" => ElevationPreference.Minimize,
			"seekClimbs" => ElevationPreference.SeekClimbs,
			_ => (ElevationPreference?)null
		};

		if (shape is null) errors["shape"] = [request.Shape is null ? "required" : "invalid_value"];
		if (profile is null) errors["profile"] = [request.Profile is null ? "required" : "invalid_value"];
		if (elevation is null) errors["elevation"] = ["invalid_value"];

		Distance? distance = null;
		if (request.TargetDistanceMeters is double meters)
		{
			if (!double.IsFinite(meters)) errors["targetDistanceMeters"] = ["out_of_range"];
			else if (meters <= 0) errors["targetDistanceMeters"] = ["must_be_positive"];
			else distance = new Distance(meters);
		}

		TimeSpan? duration = null;
		if (request.TargetDurationSeconds is long seconds)
		{
			if (seconds <= 0) errors["targetDurationSeconds"] = ["must_be_positive"];
			else if (seconds > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond)
				errors["targetDurationSeconds"] = ["out_of_range"];
			else duration = TimeSpan.FromTicks(seconds * TimeSpan.TicksPerSecond);
		}

		if (shape != RouteShape.PointToPoint && request.TargetDistanceMeters is null && request.TargetDurationSeconds is null)
		{
			errors["targetDistanceMeters"] = ["target_required"];
			errors["targetDurationSeconds"] = ["target_required"];
		}

		GeoCoordinate? destination = null;
		if (shape == RouteShape.Loop && request.Destination is not null)
		{
			errors["destination"] = ["destination_not_allowed"];
		}
		else if (shape == RouteShape.PointToPoint || request.Destination is not null)
		{
			destination = ReadCoordinate(request.Destination, "destination", errors);
			if (destination is not null && destination == start)
				errors["destination"] = ["must_differ_from_start"];
		}

		if (errors.Count > 0) return new(null, errors);

		return new(new RouteIntent(start!, shape!.Value, profile!.Value,
			targetDistance: distance, targetDuration: duration, destination: destination,
			elevation: elevation!.Value), errors);
	}

	private static GeoCoordinate? ReadCoordinate(CoordinateRequest? value, string field, Dictionary<string, string[]> errors)
	{
		if (value is null)
		{
			errors[field] = ["required"];
			return null;
		}

		var latitudeValid = ValidateComponent(value.Latitude, -90, 90, $"{field}.latitude", errors);
		var longitudeValid = ValidateComponent(value.Longitude, -180, 180, $"{field}.longitude", errors);
		return latitudeValid && longitudeValid ? new GeoCoordinate(value.Latitude!.Value, value.Longitude!.Value) : null;
	}

	private static bool ValidateComponent(double? value, double minimum, double maximum, string field, Dictionary<string, string[]> errors)
	{
		if (value is null) errors[field] = ["required"];
		else if (!double.IsFinite(value.Value) || value < minimum || value > maximum) errors[field] = ["out_of_range"];
		else return true;
		return false;
	}
}
