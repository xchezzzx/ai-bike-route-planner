namespace CyclingRoutes.Contracts.RoutePlanning;

public sealed record RouteSegmentResponse(int FromPointIndex, int ToPointIndex, string Surface, string WayType);
