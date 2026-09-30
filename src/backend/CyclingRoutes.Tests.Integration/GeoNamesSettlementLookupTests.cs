using System.Text;
using System.Text.Json;
using CyclingRoutes.Domain.RoutePlanning;
using CyclingRoutes.Infrastructure.Naming;

namespace CyclingRoutes.Tests.Integration;

public class GeoNamesSettlementLookupTests
{
	[Theory]
	[InlineData(32.08088, 34.78057, 293397, "Tel Aviv")]
	[InlineData(32.81303, 34.99928, 294801, "Haifa")]
	[InlineData(31.76904, 35.21633, 281184, "Jerusalem")]
	public void EmbeddedSubset_ResolvesRealSettlements(double latitude, double longitude, long id, string name)
	{
		var lookup = GeoNamesSettlementLookup.LoadEmbedded();
		var settlement = lookup.FindNearest(new(latitude, longitude));
		Assert.NotNull(settlement);
		Assert.Equal(id, settlement.Id);
		Assert.Equal(name, settlement.AsciiName);
		Assert.Contains("GeoNames", lookup.Attribution);
		Assert.Contains("CC BY 4.0", lookup.Attribution);
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(51.5074, -0.1278)]
	[InlineData(32, 34)]
	public void EmbeddedSubset_DoesNotInventPlacesOutsideCoverage(double latitude, double longitude) =>
		Assert.Null(GeoNamesSettlementLookup.LoadEmbedded().FindNearest(new(latitude, longitude)));

	[Theory]
	[InlineData(9999, true)]
	[InlineData(10000, true)]
	[InlineData(10001, false)]
	[InlineData(10000.001, false)]
	public void Lookup_EnforcesTenKilometreRadius(double meters, bool found)
	{
		using var data = Data(new { id = 1, asciiName = "Alpha", latitude = 0d, longitude = 0d });
		var lookup = new GeoNamesSettlementLookup(data);
		Assert.Equal(found, lookup.FindNearest(new(meters / 6371008.8 * 180 / Math.PI, 0)) is not null);
	}

	[Fact]
	public void Lookup_ChoosesNearestThenLowestIdRegardlessOfInputOrder()
	{
		using var data = Data(new { id = 99, asciiName = "Far", latitude = 32d, longitude = 34d },
			new { id = 5, asciiName = "Later", latitude = 32.01, longitude = 34d },
			new { id = 2, asciiName = "First", latitude = 32.01, longitude = 34d });
		Assert.Equal("First", new GeoNamesSettlementLookup(data).FindNearest(new(32.011, 34))!.AsciiName);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("broken")]
	[InlineData("{}")]
	[InlineData("{\"schemaVersion\":2,\"settlements\":[]}")]
	[InlineData("{\"schemaVersion\":1,\"settlements\":[{\"id\":1,\"asciiName\":\"Invalid\",\"latitude\":91,\"longitude\":34}]}")]
	[InlineData("{\"schemaVersion\":1,\"settlements\":[null]}")]
	public void MissingOrMalformedLocalData_IsAnEmptyLookup(string? json)
	{
		using var data = json is null ? null : new MemoryStream(Encoding.UTF8.GetBytes(json));
		Assert.Null(new GeoNamesSettlementLookup(data).FindNearest(new(32, 34)));
	}

	[Fact]
	public void UnreadableLocalData_IsAnEmptyLookup()
	{
		using var data = new FailingStream();
		Assert.Null(new GeoNamesSettlementLookup(data).FindNearest(new(32, 34)));
	}

	private static MemoryStream Data(params object[] settlements) =>
		new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { schemaVersion = 1, settlements })));
	private sealed class FailingStream : MemoryStream
	{
		public override int Read(Span<byte> buffer) => throw new IOException("Missing local data");
		public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Missing local data");
	}
}
