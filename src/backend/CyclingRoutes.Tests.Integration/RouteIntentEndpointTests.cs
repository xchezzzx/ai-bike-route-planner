using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CyclingRoutes.Tests.Integration;

public class RouteIntentEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private const string Endpoint = "/api/route-intents/validate";
	private const string ValidLoop = """
		{"start":{"latitude":32.0853,"longitude":34.7818},"shape":"loop","profile":"road","targetDistanceMeters":40000}
		""";
	private readonly WebApplicationFactory<Program> _factory;

	public RouteIntentEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

	[Fact]
	public async Task ValidateLoop_ReturnsExplicitUnitsAndDefaultElevation()
	{
		using var client = CreateClient();
		using var response = await Post(client, ValidLoop);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
		using var body = await ReadBody(response);
		Assert.Equal(40000, body.RootElement.GetProperty("targetDistanceMeters").GetDouble());
		Assert.Equal(32.0853, body.RootElement.GetProperty("start").GetProperty("latitude").GetDouble());
		Assert.Equal(34.7818, body.RootElement.GetProperty("start").GetProperty("longitude").GetDouble());
		Assert.Equal("balanced", body.RootElement.GetProperty("elevation").GetString());
		Assert.Equal("loop", body.RootElement.GetProperty("shape").GetString());
		Assert.Equal("road", body.RootElement.GetProperty("profile").GetString());
		Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("destination").ValueKind);
		Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("targetDurationSeconds").ValueKind);
		Assert.False(body.RootElement.TryGetProperty("geometry", out _));
	}

	[Theory]
	[InlineData("minimize")]
	[InlineData("balanced")]
	[InlineData("seekClimbs")]
	public async Task ValidatePointToPoint_AcceptsDurationOnlyAndPreservesPreferences(string elevation)
	{
		var request = JsonNode.Parse(ValidLoop)!.AsObject();
		request["shape"] = "pointToPoint";
		request["profile"] = "gravel";
		request["elevation"] = elevation;
		request["destination"] = JsonNode.Parse("{\"latitude\":32.1,\"longitude\":34.9}");
		request.Remove("targetDistanceMeters");
		request["targetDurationSeconds"] = 7200;
		using var client = CreateClient();
		using var response = await Post(client, request.ToJsonString());
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		using var body = await ReadBody(response);
		Assert.Equal(7200, body.RootElement.GetProperty("targetDurationSeconds").GetInt64());
		Assert.Equal("pointToPoint", body.RootElement.GetProperty("shape").GetString());
		Assert.Equal("gravel", body.RootElement.GetProperty("profile").GetString());
		Assert.Equal(elevation, body.RootElement.GetProperty("elevation").GetString());
		Assert.Equal(32.1, body.RootElement.GetProperty("destination").GetProperty("latitude").GetDouble());
		Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("targetDistanceMeters").ValueKind);
	}

	[Fact]
	public async Task ValidatePointToPoint_WithoutTargetsReturnsNullTargets()
	{
		using var client = CreateClient();
		using var response = await Post(client, """
			{"start":{"latitude":32.0853,"longitude":34.7818},"destination":{"latitude":32.1,"longitude":34.9},"shape":"pointToPoint","profile":"road"}
			""");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		using var body = await ReadBody(response);
		Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("targetDistanceMeters").ValueKind);
		Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("targetDurationSeconds").ValueKind);
	}

	[Theory]
	[InlineData("start", "null", "start", "required")]
	[InlineData("start", "{}", "start.latitude", "required")]
	[InlineData("start", "{\"latitude\":0}", "start.longitude", "required")]
	[InlineData("start", "{\"latitude\":91,\"longitude\":0}", "start.latitude", "out_of_range")]
	[InlineData("shape", "null", "shape", "required")]
	[InlineData("shape", "\"unknown\"", "shape", "invalid_value")]
	[InlineData("shape", "\"pointToPoint\"", "destination", "required")]
	[InlineData("profile", "null", "profile", "required")]
	[InlineData("profile", "\"Road\"", "profile", "invalid_value")]
	[InlineData("elevation", "\"flat\"", "elevation", "invalid_value")]
	[InlineData("targetDistanceMeters", "0", "targetDistanceMeters", "must_be_positive")]
	[InlineData("targetDistanceMeters", "null", "targetDurationSeconds", "target_required")]
	[InlineData("targetDurationSeconds", "-1", "targetDurationSeconds", "must_be_positive")]
	[InlineData("targetDurationSeconds", "922337203686", "targetDurationSeconds", "out_of_range")]
	[InlineData("destination", "{\"latitude\":32.1,\"longitude\":34.9}", "destination", "destination_not_allowed")]
	public async Task InvalidPreferences_ReturnFieldError(string property, string value, string field, string code)
	{
		var request = JsonNode.Parse(ValidLoop)!.AsObject();
		request[property] = JsonNode.Parse(value);
		using var client = CreateClient();
		using var response = await Post(client, request.ToJsonString());
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
		using var body = await ReadBody(response);
		Assert.Equal(400, body.RootElement.GetProperty("status").GetInt32());
		Assert.Contains(code, body.RootElement.GetProperty("errors").GetProperty(field)
			.EnumerateArray().Select(error => error.GetString()));
	}

	[Theory]
	[InlineData("Development", "{", "application/json", 400)]
	[InlineData("Production", "{", "application/json", 400)]
	[InlineData("Development", "null", "application/json", 400)]
	[InlineData("Production", "", "application/json", 400)]
	[InlineData("Production", "{\"shape\":1}", "application/json", 400)]
	[InlineData("Production", "{\"targetDurationSeconds\":1.5}", "application/json", 400)]
	[InlineData("Production", "{\"unexpected\":true}", "application/json", 400)]
	[InlineData("Production", "{\"start\":{\"lat\":32}}", "application/json", 400)]
	[InlineData("Production", "{\"targetDistanceMeters\":\"40000\"}", "application/json", 400)]
	[InlineData("Production", "{\"targetDurationSeconds\":9223372036854775808}", "application/json", 400)]
	[InlineData("Production", "{}", "text/plain", 415)]
	public async Task BindingFailures_ReturnProblemDetailsWithoutInternals(string environment, string json, string mediaType, int status)
	{
		using var factory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
		using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
		using var content = new StringContent(json, Encoding.UTF8, mediaType);
		using var response = await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);
		Assert.Equal(status, (int)response.StatusCode);
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
		using var body = await ReadBody(response);
		Assert.Equal(status, body.RootElement.GetProperty("status").GetInt32());
		Assert.False(body.RootElement.TryGetProperty("exception", out _));
		Assert.False(body.RootElement.TryGetProperty("detail", out _));
	}

	private HttpClient CreateClient() => _factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

	private static async Task<HttpResponseMessage> Post(HttpClient client, string json)
	{
		using var content = new StringContent(json, Encoding.UTF8, "application/json");
		return await client.PostAsync(Endpoint, content, TestContext.Current.CancellationToken);
	}

	private static async Task<JsonDocument> ReadBody(HttpResponseMessage response) =>
		JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
}
