namespace CyclingRoutes.Domain.RoutePlanning;

public sealed record DurationRange
{
	public long Min { get; }
	public long Max { get; }
	public double Midpoint => Min + (Max - Min) / 2d;

	public DurationRange(long min, long max)
	{
		if (min <= 0 || min > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond)
			throw new ArgumentOutOfRangeException(nameof(min));
		if (max < min || max > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond)
			throw new ArgumentOutOfRangeException(nameof(max));
		Min = min;
		Max = max;
	}

	public double Delta(double actual)
	{
		if (!double.IsFinite(actual)) throw new ArgumentOutOfRangeException(nameof(actual));
		return actual < Min ? actual - Min : actual > Max ? actual - Max : 0;
	}
}
