using System.Text.Json.Serialization;

namespace CyclingRoutes.Contracts.RoutePlanning;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DistanceRangeRequest(double? Min, double? Max);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DurationRangeRequest(long? Min, long? Max);

public sealed record DistanceRangeResponse(double Min, double Max);
public sealed record DurationRangeResponse(long Min, long Max);
