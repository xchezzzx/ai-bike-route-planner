using System.Text.Json.Serialization;

namespace CyclingRoutes.Contracts.RoutePlanning;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InterpretRouteIntentRequest
{
	public string? Prompt { get; init; }
	public string? Locale { get; init; }
	public CoordinateRequest? Start { get; init; }
	public CoordinateRequest? Destination { get; init; }
}
