using System.Globalization;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class RouteNameResolverTests
{
	[Theory]
	[InlineData(RouteShape.Loop, 1, 2, "Alpha-loop-road-40")]
	[InlineData(RouteShape.PointToPoint, 1, 2, "Alpha-Beta-road-40")]
	[InlineData(RouteShape.PointToPoint, 1, 1, "Alpha-road-40")]
	[InlineData(RouteShape.PointToPoint, 0, 2, "Unknown-Beta-road-40")]
	[InlineData(RouteShape.PointToPoint, 1, 0, "Alpha-Unknown-road-40")]
	[InlineData(RouteShape.PointToPoint, 0, 0, "Route-road-40")]
	[InlineData(RouteShape.Loop, 0, 2, "Route-loop-road-40")]
	public void Resolve_UsesShapeAndStableSettlementIds(RouteShape shape, int start, int end, string expected)
	{
		var lookup = new Lookup(Place(start), Place(end));
		var result = new RouteNameResolver(lookup).Resolve(Path(40000), shape, CyclingProfile.Road);
		Assert.Equal(expected, result.Value);
		Assert.Equal(start != 0 || (shape != RouteShape.Loop && end != 0) ? "GeoNames test attribution" : null, result.Attribution);
		Assert.Equal(shape == RouteShape.Loop ? new[] { new GeoCoordinate(32, 34) } : new[] { new GeoCoordinate(32, 34), new GeoCoordinate(33, 35) }, lookup.Positions);
	}

	[Theory]
	[InlineData(39499.9, "39")]
	[InlineData(39500, "40")]
	[InlineData(40000, "40")]
	[InlineData(40500, "41")]
	[InlineData(104500, "105")]
	[InlineData(499, "0")]
	[InlineData(500, "1")]
	public void Resolve_UsesActualKilometresRoundedAwayFromZero(double meters, string expected)
	{
		var result = new RouteNameResolver(new Lookup(null, null)).Resolve(Path(meters), RouteShape.Loop, CyclingProfile.Road);
		Assert.Equal($"Route-loop-road-{expected}", result.Value);
	}

	[Theory]
	[InlineData("en-US")]
	[InlineData("fr-FR")]
	[InlineData("he-IL")]
	[InlineData("ar-SA")]
	public void Resolve_IsCultureIndependentAndUsesProfile(string culture)
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
			Assert.Equal("Alpha-loop-gravel-41", new RouteNameResolver(new Lookup(Place(1), null))
				.Resolve(Path(40500), RouteShape.Loop, CyclingProfile.Gravel).Value);
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	[Theory]
	[InlineData("  Tel Aviv-Yafo / East  ", "Tel-Aviv-Yafo-East")]
	[InlineData("../CON:<bad>\\name|?*\"", "CON-bad-name")]
	[InlineData("Be'er  Sheva", "Be-er-Sheva")]
	[InlineData("\u00c9vry\u2014Centre", "Evry-Centre")]
	[InlineData("\u05ea\u05dc \u05d0\u05d1\u05d9\u05d1", "Route")]
	[InlineData(" . / : ", "Route")]
	[InlineData("", "Route")]
	public void Resolve_ProducesSafeLatinFileStemWithoutInventingTransliterations(string label, string expected)
	{
		var result = new RouteNameResolver(new Lookup(new(1, label), null)).Resolve(Path(40000), RouteShape.Loop, CyclingProfile.Road);
		Assert.Equal(expected + "-loop-road-40", result.Value);
		Assert.Matches("^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$", result.Value);
		Assert.Equal(expected == "Route" ? null : "GeoNames test attribution", result.Attribution);
	}

	[Fact]
	public void Resolve_DistinctIdsWithSameLabelAreNotCollapsed()
	{
		Assert.Equal("Same-Same-road-40", new RouteNameResolver(new Lookup(new(1, "Same"), new(2, "Same")))
			.Resolve(Path(40000), RouteShape.PointToPoint, CyclingProfile.Road).Value);
	}

	[Fact]
	public void Resolve_BoundsBothPlaceTokensWithoutLosingSuffix()
	{
		var result = new RouteNameResolver(new Lookup(new(1, new string('A', 200)), new(2, new string('B', 200))))
			.Resolve(Path(105000), RouteShape.PointToPoint, CyclingProfile.Road);
		Assert.Equal(new string('A', 40) + "-" + new string('B', 40) + "-road-105", result.Value);
		Assert.True(result.Value.Length <= 120);
	}

	[Fact]
	public void Resolve_EmptyGeometryFallsBackWithoutLookup()
	{
		var lookup = new Lookup(Place(1), Place(2));
		Assert.Equal("Route-road-40", new RouteNameResolver(lookup).Resolve(Path(40000) with { Points = [] }, RouteShape.PointToPoint, CyclingProfile.Road).Value);
		Assert.Empty(lookup.Positions);
	}

	[Theory]
	[InlineData(double.MaxValue)]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(-1)]
	public void Resolve_InvalidMetricsDoNotBreakTheFilenameBound(double meters)
	{
		var result = new RouteNameResolver(new Lookup(Place(1), Place(2))).Resolve(Path(meters), RouteShape.PointToPoint, CyclingProfile.Road);
		Assert.Equal("Alpha-Beta-road-unknown", result.Value);
		Assert.True(result.Value.Length <= 120);
	}

	private static SettlementName? Place(int id) => id == 0 ? null : new(id, id == 1 ? "Alpha" : "Beta");
	private static RoutedPath Path(double meters) => new([new(new(32, 34), 1), new(new(33, 35), 2)], meters, 100, 1, 1, "ORS");
	private sealed class Lookup(SettlementName? start, SettlementName? end) : ISettlementLookup
	{
		public List<GeoCoordinate> Positions { get; } = [];
		public string Attribution => "GeoNames test attribution";
		public SettlementName? FindNearest(GeoCoordinate position)
		{
			Positions.Add(position);
			return position == new GeoCoordinate(32, 34) ? start : end;
		}
	}
}
