using System.Text.Json.Serialization;

namespace CyclingRoutes.Contracts.RoutePlanning;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CoordinateRequest
{
	public double? Latitude { get; init; }
	public double? Longitude { get; init; }
}
