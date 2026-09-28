using System.Globalization;
using System.Xml.Linq;

namespace CyclingRoutes.Application.Routing;

public static class GpxWriter
{
	private static readonly XNamespace Namespace = "http://www.topografix.com/GPX/1/1";

	public static string Write(RoutedPath path)
	{
		var points = path.Points.Select(point => new XElement(Namespace + "trkpt",
			new XAttribute("lat", Format(point.Position.Latitude)),
			new XAttribute("lon", Format(point.Position.Longitude == 180 ? -180 : point.Position.Longitude)),
			point.ElevationMeters is double elevation ? new XElement(Namespace + "ele", Format(elevation)) : null));
		var document = new XDocument(new XElement(Namespace + "gpx",
			new XAttribute("version", "1.1"), new XAttribute("creator", "CyclingRoutes"),
			new XElement(Namespace + "metadata", new XElement(Namespace + "desc", path.Attribution)),
			new XElement(Namespace + "trk", new XElement(Namespace + "name", "Cycling route"),
				new XElement(Namespace + "trkseg", points))));
		return document.ToString(SaveOptions.DisableFormatting);
	}

	private static string Format(double value)
	{
		// Preserve round-trip digits, expanding exponents because GPX requires xsd:decimal.
		var text = value.ToString("R", CultureInfo.InvariantCulture);
		var exponentIndex = text.IndexOf('E');
		if (exponentIndex < 0) return text;
		var exponent = int.Parse(text[(exponentIndex + 1)..], CultureInfo.InvariantCulture);
		var negative = text.StartsWith('-');
		var mantissa = text[(negative ? 1 : 0)..exponentIndex];
		var dot = mantissa.IndexOf('.');
		var decimalPosition = (dot < 0 ? mantissa.Length : dot) + exponent;
		var digits = mantissa.Replace(".", "");
		var expanded = decimalPosition <= 0
			? "0." + new string('0', -decimalPosition) + digits
			: decimalPosition >= digits.Length
				? digits + new string('0', decimalPosition - digits.Length)
				: digits.Insert(decimalPosition, ".");
		return negative ? "-" + expanded : expanded;
	}
}
