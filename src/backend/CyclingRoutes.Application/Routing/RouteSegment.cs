namespace CyclingRoutes.Application.Routing;

public enum RouteSurface { Unknown, Asphalt, Paved, Unpaved, Other }
public enum RouteWayType { Unknown, StateRoad, Road, Street, Path, Track, Cycleway, Footway, Steps, Ferry, Construction }

public sealed record RouteSegment(int FromPointIndex, int ToPointIndex, RouteSurface Surface, RouteWayType WayType);
