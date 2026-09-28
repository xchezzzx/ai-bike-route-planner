using System.Globalization;
using System.Xml.Linq;
using CyclingRoutes.Application.Routing;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class GpxWriterTests
{
	[Theory]
	[InlineData(32.08531234567891, 179.99999999999997)]
	[InlineData(double.Epsilon, -0.00000001)]
	[InlineData(-0.00000001, 0.00000001)]
	public void Export_RoundTripsCoordinatesWithoutRoundingToInvalidLongitude(double latitude, double longitude)
	{
		var route = new RoutedPath([new(new(latitude, longitude), double.MaxValue), new(new(0, 0), null)], 1, 1, null, null, "ORS");
		var document = XDocument.Parse(GpxWriter.Write(route));
		XNamespace ns = "http://www.topografix.com/GPX/1/1";
		var point = document.Descendants(ns + "trkpt").First();
		var lat = point.Attribute("lat")!.Value;
		var lon = point.Attribute("lon")!.Value;
		var ele = point.Element(ns + "ele")!.Value;
		Assert.Equal(latitude, double.Parse(lat, CultureInfo.InvariantCulture));
		Assert.Equal(longitude, double.Parse(lon, CultureInfo.InvariantCulture));
		Assert.Equal(double.MaxValue, double.Parse(ele, CultureInfo.InvariantCulture));
		Assert.DoesNotContain("E", lat);
		Assert.DoesNotContain("E", lon);
		Assert.DoesNotContain("E", ele);
	}

	[Theory]
	[InlineData("en-US")]
	[InlineData("ru-RU")]
	[InlineData("he-IL")]
	public void Export_PreservesGeometryAndEscapesMetadata(string culture)
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
			var route = new RoutedPath(
				[new(new(32.0853, 34.7818), -12.5), new(new(32.1, 34.82), null)],
				4567.8, 987.6, null, null, "ORS & OSM <contributors>");
			var document = XDocument.Parse(GpxWriter.Write(route));
			XNamespace ns = "http://www.topografix.com/GPX/1/1";
			Assert.Equal(ns + "gpx", document.Root!.Name);
			Assert.Equal("1.1", document.Root.Attribute("version")!.Value);
			Assert.Equal("ORS & OSM <contributors>", document.Root.Element(ns + "metadata")!.Element(ns + "desc")!.Value);
			var segment = Assert.Single(document.Descendants(ns + "trkseg"));
			var points = segment.Elements(ns + "trkpt").ToArray();
			Assert.Equal(2, points.Length);
			Assert.Equal("32.0853", points[0].Attribute("lat")!.Value);
			Assert.Equal("34.7818", points[0].Attribute("lon")!.Value);
			Assert.Equal("-12.5", points[0].Element(ns + "ele")!.Value);
			Assert.Equal("32.1", points[1].Attribute("lat")!.Value);
			Assert.Null(points[1].Element(ns + "ele"));
			Assert.Empty(document.Descendants(ns + "time"));
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	[Fact]
	public void Export_UsesGpxDecimalSyntaxAndExclusiveLongitudeBound()
	{
		var route = new RoutedPath([new(new(0.00000001, 180), null), new(new(0, -180), null)], 1, 1, null, null, "ORS");
		var document = XDocument.Parse(GpxWriter.Write(route));
		XNamespace ns = "http://www.topografix.com/GPX/1/1";
		var first = document.Descendants(ns + "trkpt").First();
		Assert.Equal("0.00000001", first.Attribute("lat")!.Value);
		Assert.Equal("-180", first.Attribute("lon")!.Value);
	}
}
