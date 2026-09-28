namespace CyclingRoutes.Infrastructure.Interpretation;

public sealed record GeminiOptions
{
	public string ApiKey { get; init; } = "";
	public string Model { get; init; } = "";
}
