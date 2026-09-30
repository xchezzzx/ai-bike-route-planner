using System.Xml.Linq;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class GeneratedRouteNamingTests
{
	[Fact]
	public void Create_PreservesProviderPathAndWarningsWhileSharingOneExportName()
	{
		var path = new RoutedPath([new(new(32, 34), -1), new(new(33, 35), null)], 104500, 400, 12, 13, "Provider & OSM");
		var intent = new RouteIntent(new(30, 34), RouteShape.PointToPoint, CyclingProfile.Road, destination: new(31, 35));
		string[] warnings = ["targets_not_optimized"];
		var route = GeneratedRoute.Create(path, intent, warnings, new(new Lookup()));
		Assert.Equal("Actual-Other-road-105", route.Name);
		Assert.Equal("Provider & OSM", path.Attribution);
		Assert.Equal(path with { Attribution = "Provider & OSM\nGeoNames <test>" }, route.Path);
		Assert.Same(path.Points, route.Path.Points);
		Assert.Same(warnings, route.Warnings);
		XNamespace ns = "http://www.topografix.com/GPX/1/1";
		var document = XDocument.Parse(route.Gpx);
		Assert.Equal(route.Name, document.Root!.Element(ns + "trk")!.Element(ns + "name")!.Value);
		Assert.Equal("Provider & OSM\nGeoNames <test>", document.Root.Element(ns + "metadata")!.Element(ns + "desc")!.Value);
	}

	private sealed class Lookup : ISettlementLookup
	{
		public string Attribution => "GeoNames <test>";
		public SettlementName FindNearest(GeoCoordinate position) => position.Latitude == 32 ? new(1, "Actual") : new(2, "Other");
	}
}
