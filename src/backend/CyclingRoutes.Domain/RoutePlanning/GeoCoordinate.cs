namespace CyclingRoutes.Domain.RoutePlanning;

public sealed record GeoCoordinate
{
	public double Latitude { get; }
	public double Longitude { get; }

	public GeoCoordinate(double latitude, double longitude)
	{
		if (!double.IsFinite(latitude) || latitude < -90 || latitude > 90)
		{
			throw new ArgumentOutOfRangeException(nameof(latitude), latitude,
				"Latitude must be finite and between -90 and 90 degrees.");
		}

		if (!double.IsFinite(longitude) || longitude < -180 || longitude > 180)
		{
			throw new ArgumentOutOfRangeException(nameof(longitude), longitude,
				"Longitude must be finite and between -180 and 180 degrees.");
		}

		Latitude = latitude;
		Longitude = longitude;
	}
}
