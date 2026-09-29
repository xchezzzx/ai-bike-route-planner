using System.Text.Json.Serialization;

namespace CyclingRoutes.Contracts.RoutePlanning;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RouteIntentRequest
{
	public CoordinateRequest? Start { get; init; }
	public CoordinateRequest? Destination { get; init; }
	public string? Shape { get; init; }
	public string? Profile { get; init; }
	public string? Elevation { get; init; }
	public double? TargetDistanceMeters { get; init; }
	public long? TargetDurationSeconds { get; init; }
	public DistanceRangeRequest? TargetDistanceRangeMeters { get; init; }
	public DurationRangeRequest? TargetDurationRangeSeconds { get; init; }
}
