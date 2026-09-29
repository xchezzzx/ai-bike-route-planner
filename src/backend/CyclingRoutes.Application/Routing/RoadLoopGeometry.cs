namespace CyclingRoutes.Application.Routing;

internal static class RoadLoopGeometry
{
	public static void Validate(RoutedPath path)
	{
		if (path.Points.Count < 4 || path.Points[0].Position != path.Points[^1].Position
			|| path.Points.Select(x => x.Position).Distinct().Take(3).Count() < 3)
			throw new RoutingException(RoutingFailure.InvalidResponse);
	}

	public static bool SameGeometry(RoutedPath left, RoutedPath right)
	{
		if (left.Points.Count != right.Points.Count) return false;
		var forward = true;
		var reverse = true;
		for (var i = 0; i < left.Points.Count && (forward || reverse); i++)
		{
			forward &= left.Points[i].Position == right.Points[i].Position;
			reverse &= left.Points[i].Position == right.Points[^(i + 1)].Position;
		}
		return forward || reverse;
	}
}
