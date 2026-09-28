namespace CyclingRoutes.Domain.RoutePlanning;

public sealed record Distance
{
	public double Meters { get; }

	public Distance(double meters)
	{
		if (!double.IsFinite(meters) || meters <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(meters), meters,
				"Distance must be finite and greater than zero meters.");
		}

		Meters = meters;
	}
}
