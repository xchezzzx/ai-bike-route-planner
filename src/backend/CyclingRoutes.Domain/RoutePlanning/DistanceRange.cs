namespace CyclingRoutes.Domain.RoutePlanning;

public sealed record DistanceRange
{
	public double Min { get; }
	public double Max { get; }
	public double Midpoint => Min + (Max - Min) / 2;

	public DistanceRange(double min, double max)
	{
		if (!double.IsFinite(min) || min <= 0) throw new ArgumentOutOfRangeException(nameof(min));
		if (!double.IsFinite(max) || max < min) throw new ArgumentOutOfRangeException(nameof(max));
		Min = min;
		Max = max;
	}

	public double Delta(double actual)
	{
		if (!double.IsFinite(actual)) throw new ArgumentOutOfRangeException(nameof(actual));
		return actual < Min ? actual - Min : actual > Max ? actual - Max : 0;
	}
}
