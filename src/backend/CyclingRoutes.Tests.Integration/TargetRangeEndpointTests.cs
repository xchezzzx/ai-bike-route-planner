using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CyclingRoutes.Tests.Integration;

public class TargetRangeEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly WebApplicationFactory<Program> factory;
	public TargetRangeEndpointTests(WebApplicationFactory<Program> factory) => this.factory = factory;

	[Theory]
	[InlineData("targetDistanceRangeMeters", "{\"min\":18000.5,\"max\":22000.5}")]
	[InlineData("targetDistanceRangeMeters", "{\"min\":20000,\"max\":20000}")]
	[InlineData("targetDurationRangeSeconds", "{\"min\":3000,\"max\":4200}")]
	[InlineData("targetDurationRangeSeconds", "{\"min\":922337203685,\"max\":922337203685}")]
	public async Task RangeOnlyLoopPreservesBothBounds(string field, string range)
	{
		using var response = await Post("/api/route-intents/validate", $"\"{field}\":{range}");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
		using var expected = JsonDocument.Parse(range);
		Assert.Equal(expected.RootElement.GetProperty("min").GetDouble(), json.RootElement.GetProperty(field).GetProperty("min").GetDouble());
		Assert.Equal(expected.RootElement.GetProperty("max").GetDouble(), json.RootElement.GetProperty(field).GetProperty("max").GetDouble());
		Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("targetDistanceMeters").ValueKind);
		Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("targetDurationSeconds").ValueKind);
	}

	[Theory]
	[InlineData("\"targetDistanceMeters\":20000,\"targetDistanceRangeMeters\":null,\"targetDurationRangeSeconds\":null")]
	[InlineData("\"targetDistanceRangeMeters\":{\"min\":18000,\"max\":22000},\"targetDurationSeconds\":3600")]
	[InlineData("\"targetDistanceMeters\":20000,\"targetDurationRangeSeconds\":{\"min\":3000,\"max\":4200}")]
	public async Task NullRangesAndMixedMetricsRemainValid(string fields)
	{
		using var response = await Post("/api/route-intents/validate", fields);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	public static TheoryData<string> InvalidFields => new()
	{
		"\"targetDistanceRangeMeters\":{}",
		"\"targetDistanceRangeMeters\":{\"min\":1}",
		"\"targetDistanceRangeMeters\":{\"max\":2}",
		"\"targetDistanceRangeMeters\":{\"min\":null,\"max\":2}",
		"\"targetDistanceRangeMeters\":{\"min\":0,\"max\":2}",
		"\"targetDistanceRangeMeters\":{\"min\":3,\"max\":2}",
		"\"targetDistanceRangeMeters\":{\"min\":1,\"max\":1e999}",
		"\"targetDistanceRangeMeters\":{\"min\":\"1\",\"max\":2}",
		"\"targetDistanceRangeMeters\":{\"min\":1,\"max\":2,\"extra\":3}",
		"\"targetDistanceRangeMeters\":{\"min\":1,\"min\":1,\"max\":2}",
		"\"targetDistanceRangeMeters\":{\"min\":1,\"Min\":1,\"max\":2}",
		"\"targetDurationRangeSeconds\":{\"min\":1.5,\"max\":2}",
		"\"targetDurationRangeSeconds\":{\"min\":-1,\"max\":2}",
		"\"targetDurationRangeSeconds\":{\"min\":1,\"max\":922337203686}",
		"\"targetDurationRangeSeconds\":{\"min\":1,\"max\":9223372036854775808}",
		"\"targetDurationRangeSeconds\":{\"min\":1,\"max\":2,\"max\":2}",
		"\"targetDistanceMeters\":20000,\"targetDistanceRangeMeters\":{\"min\":18000,\"max\":22000}",
		"\"targetDurationSeconds\":3600,\"targetDurationRangeSeconds\":{\"min\":3000,\"max\":4200}",
		"\"targetDistanceRangeMeters\":null,\"targetDurationRangeSeconds\":null"
	};

	[Theory]
	[InlineData("\"targetDistanceRangeMeters\":{\"min\":2,\"max\":1}", "targetDistanceRangeMeters", "range_reversed")]
	[InlineData("\"targetDurationRangeSeconds\":{\"min\":2,\"max\":1}", "targetDurationRangeSeconds", "range_reversed")]
	[InlineData("\"targetDistanceMeters\":1,\"targetDistanceRangeMeters\":{\"min\":1,\"max\":2}", "targetDistanceRangeMeters", "target_conflict")]
	[InlineData("\"targetDurationSeconds\":1,\"targetDurationRangeSeconds\":{\"min\":1,\"max\":2}", "targetDurationRangeSeconds", "target_conflict")]
	[InlineData("\"targetDistanceRangeMeters\":{\"max\":2}", "targetDistanceRangeMeters.min", "required")]
	[InlineData("\"targetDurationRangeSeconds\":{\"min\":1,\"max\":null}", "targetDurationRangeSeconds.max", "required")]
	[InlineData("\"targetDistanceRangeMeters\":{\"min\":0,\"max\":2}", "targetDistanceRangeMeters.min", "must_be_positive")]
	[InlineData("\"targetDurationRangeSeconds\":{\"min\":1,\"max\":922337203686}", "targetDurationRangeSeconds.max", "out_of_range")]
	public async Task SemanticErrorsUseStableFrontendCodes(string fields, string field, string code)
	{
		using var response = await Post("/api/route-intents/validate", fields);
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
		Assert.Equal(code, json.RootElement.GetProperty("errors").GetProperty(field)[0].GetString());
	}

	[Theory]
	[MemberData(nameof(InvalidFields))]
	public async Task InvalidRangesAreRejectedBeforeAnyProviderCall(string fields)
	{
		foreach (var endpoint in new[] { "/api/route-intents/validate", "/api/routes/plan", "/api/routes/candidates", "/api/routes/generate" })
		{
			using var response = await Post(endpoint, fields);
			Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		}
	}

	private async Task<HttpResponseMessage> Post(string endpoint, string fields)
	{
		using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
		using var body = new StringContent("{\"start\":{\"latitude\":32,\"longitude\":34},\"shape\":\"loop\",\"profile\":\"road\"," + fields + "}", Encoding.UTF8, "application/json");
		return await client.PostAsync(endpoint, body, TestContext.Current.CancellationToken);
	}
}
