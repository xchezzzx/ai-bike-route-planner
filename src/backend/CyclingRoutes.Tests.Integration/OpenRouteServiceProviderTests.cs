using System.Net;
using System.Text;
using System.Text.Json;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;
using CyclingRoutes.Infrastructure.Routing;

namespace CyclingRoutes.Tests.Integration;

public class OpenRouteServiceProviderTests
{
	internal const string ValidResponse = """
		{"type":"FeatureCollection","features":[{"type":"Feature","geometry":{"type":"LineString","coordinates":[[34.7818,32.0853,12.5],[34.82,32.1,20]]},"properties":{"summary":{"distance":4567.8,"duration":987.6},"ascent":15,"descent":7.5}}]}
		""";
	private static readonly GeoCoordinate Start = new(32.0853, 34.7818);
	private static readonly GeoCoordinate Destination = new(32.1, 34.82);

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task RoadLoop_ParsesClosed2DAnd3DGeometry(bool withElevation)
	{
		var coordinates = withElevation ? "[[34,32,1],[35,32,2],[35,33,3],[34,32,1]]" : "[[34,32],[35,32],[35,33],[34,32]]";
		var body = $$$$"""
			{"type":"FeatureCollection","features":[{"geometry":{"type":"LineString","coordinates":{{{{coordinates}}}}},"properties":{"summary":{"distance":20000,"duration":3600}}}]}
			""";
		using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(Response(200, body))));
		var route = await new OpenRouteServiceProvider(client, new() { ApiKey = "test-key" })
			.GetRoadLoopAsync(Start, 20000, 1, TestContext.Current.CancellationToken);
		Assert.Equal(4, route.Points.Count);
		Assert.Equal(route.Points[0].Position, route.Points[^1].Position);
		Assert.Equal(withElevation ? 1d : (double?)null, route.Points[0].ElevationMeters);
		Assert.Null(route.AscentMeters);
		Assert.Null(route.DescentMeters);
	}

	[Theory]
	[InlineData("no-key", RoutingFailure.NotConfigured)]
	[InlineData("charset", RoutingFailure.InvalidResponse)]
	[InlineData("malformed", RoutingFailure.InvalidResponse)]
	[InlineData("no-route", RoutingFailure.NoRoute)]
	[InlineData("quota", RoutingFailure.RateLimited)]
	[InlineData("credentials", RoutingFailure.CredentialsRejected)]
	[InlineData("limit", RoutingFailure.LimitExceeded)]
	[InlineData("timeout", RoutingFailure.Timeout)]
	[InlineData("network", RoutingFailure.Unavailable)]
	public async Task RoadLoop_PreservesSharedFailureHandling(string scenario, RoutingFailure expected)
	{
		using var client = new HttpClient(new StubHandler((_, _) =>
		{
			Assert.NotEqual("no-key", scenario);
			if (scenario == "timeout") throw new TaskCanceledException();
			if (scenario == "network") throw new HttpRequestException("private transport detail");
			var response = scenario switch
			{
				"no-route" => Response(404, "{\"error\":{\"code\":2009}}"),
				"limit" => Response(400, "{\"error\":{\"code\":2004}}"),
				"quota" => Response(429, "private detail"),
				"credentials" => Response(403, "private detail"),
				"malformed" => Response(200, "{}"),
				_ => Response(200, ValidResponse)
			};
			if (scenario == "charset") response.Content.Headers.ContentType!.CharSet = "invalid-charset";
			return Task.FromResult(response);
		}));
		var provider = new OpenRouteServiceProvider(client, new() { ApiKey = scenario == "no-key" ? "" : "test-key" });
		var error = await Assert.ThrowsAsync<RoutingException>(() => provider.GetRoadLoopAsync(Start, 20000, 1, TestContext.Current.CancellationToken));
		Assert.Equal(expected, error.Failure);
		Assert.DoesNotContain("private", error.ToString());
	}

	[Fact]
	public async Task RoadLoop_CallerCancellationPropagates()
	{
		using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		using var client = new HttpClient(new StubHandler(async (_, ct) =>
		{
			source.Cancel();
			await Task.Delay(Timeout.Infinite, ct);
			return Response(200, ValidResponse);
		}));
		var provider = new OpenRouteServiceProvider(client, new() { ApiKey = "test-key" });
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetRoadLoopAsync(Start, 20000, 1, source.Token));
	}

	[Fact]
	public async Task RoadLoop_SendsSingleCoordinateAndRoundTripOptions()
	{
		using var handler = new StubHandler(async (request, ct) =>
		{
			Assert.Equal("https://api.heigit.org/openrouteservice/v2/directions/cycling-road/geojson", request.RequestUri!.AbsoluteUri);
			Assert.Equal(HttpMethod.Post, request.Method);
			Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("Authorization")));
			using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
			var root = json.RootElement;
			Assert.Equal(1, root.GetProperty("coordinates").GetArrayLength());
			Assert.Equal(34.7818, root.GetProperty("coordinates")[0][0].GetDouble());
			Assert.Equal(32.0853, root.GetProperty("coordinates")[0][1].GetDouble());
			Assert.True(root.GetProperty("elevation").GetBoolean());
			Assert.False(root.GetProperty("instructions").GetBoolean());
			Assert.Equal("m", root.GetProperty("units").GetString());
			var options = root.GetProperty("options");
			Assert.Equal(new[] { "ferries", "fords", "steps" }, options.GetProperty("avoid_features").EnumerateArray().Select(x => x.GetString()));
			var loop = options.GetProperty("round_trip");
			Assert.Equal(20000, loop.GetProperty("length").GetDouble());
			Assert.Equal(3, loop.GetProperty("points").GetInt32());
			Assert.Equal(2, loop.GetProperty("seed").GetInt32());
			return Response(200, ValidResponse);
		});
		using var client = new HttpClient(handler);
		var provider = new OpenRouteServiceProvider(client, new() { ApiKey = "test-key" });
		var route = await provider.GetRoadLoopAsync(Start, 20000, 2, TestContext.Current.CancellationToken);
		Assert.Equal(4567.8, route.DistanceMeters);
	}

	[Fact]
	public async Task ParameterLimit_Maps2004()
	{
		using var handler = new StubHandler((_, _) => Task.FromResult(Response(400, "{\"error\":{\"code\":2004}}")));
		using var client = new HttpClient(handler);
		var provider = new OpenRouteServiceProvider(client, new() { ApiKey = "test-key" });
		var error = await Assert.ThrowsAsync<RoutingException>(() => provider.GetRoadRouteAsync(Start, Destination, TestContext.Current.CancellationToken));
		Assert.Equal(RoutingFailure.LimitExceeded, error.Failure);
	}

	[Fact]
	public async Task UnknownResponseCharset_IsAnInvalidProviderResponse()
	{
		using var handler = new StubHandler((_, _) =>
		{
			var response = Response(200, ValidResponse);
			response.Content.Headers.ContentType!.CharSet = "not-a-real-charset";
			return Task.FromResult(response);
		});
		using var client = new HttpClient(handler);
		var provider = new OpenRouteServiceProvider(client, new() { ApiKey = "test-key" });
		var error = await Assert.ThrowsAsync<RoutingException>(() => provider.GetRoadRouteAsync(Start, Destination, TestContext.Current.CancellationToken));
		Assert.Equal(RoutingFailure.InvalidResponse, error.Failure);
	}

	[Fact]
	public async Task RoadRequest_PreservesCoordinateOrderAndMapsMetrics()
	{
		using var handler = new StubHandler(async (request, ct) =>
		{
			Assert.Equal("https://api.heigit.org/openrouteservice/v2/directions/cycling-road/geojson", request.RequestUri!.AbsoluteUri);
			Assert.Equal(HttpMethod.Post, request.Method);
			Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("Authorization")));
			using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
			Assert.Equal(34.7818, json.RootElement.GetProperty("coordinates")[0][0].GetDouble());
			Assert.Equal(32.0853, json.RootElement.GetProperty("coordinates")[0][1].GetDouble());
			Assert.Equal(34.82, json.RootElement.GetProperty("coordinates")[1][0].GetDouble());
			Assert.True(json.RootElement.GetProperty("elevation").GetBoolean());
			Assert.False(json.RootElement.GetProperty("instructions").GetBoolean());
			return Response(200, ValidResponse);
		});
		using var client = new HttpClient(handler);
		var provider = new OpenRouteServiceProvider(client, new() { ApiKey = "test-key" });
		var route = await provider.GetRoadRouteAsync(Start, Destination, TestContext.Current.CancellationToken);
		Assert.Equal(2, route.Points.Count);
		Assert.Equal(Start, route.Points[0].Position);
		Assert.Equal(12.5, route.Points[0].ElevationMeters);
		Assert.Equal(Destination, route.Points[1].Position);
		Assert.Equal(4567.8, route.DistanceMeters);
		Assert.Equal(987.6, route.EstimatedDurationSeconds);
		Assert.Equal(15, route.AscentMeters);
		Assert.Equal(7.5, route.DescentMeters);
		Assert.Contains("OpenStreetMap", route.Attribution);
	}

	[Theory]
	[InlineData(401, "secret upstream body", RoutingFailure.CredentialsRejected)]
	[InlineData(403, "secret upstream body", RoutingFailure.CredentialsRejected)]
	[InlineData(429, "", RoutingFailure.RateLimited)]
	[InlineData(503, "", RoutingFailure.Unavailable)]
	[InlineData(404, "{\"error\":{\"code\":2009}}", RoutingFailure.NoRoute)]
	[InlineData(404, "{\"error\":{\"code\":2010}}", RoutingFailure.NoRoute)]
	[InlineData(400, "{\"error\":{\"code\":2003}}", RoutingFailure.InvalidResponse)]
	[InlineData(302, "", RoutingFailure.InvalidResponse)]
	public async Task ProviderErrors_AreMappedWithoutExposingBody(int status, string body, RoutingFailure failure)
	{
		using var handler = new StubHandler((_, _) => Task.FromResult(Response(status, body)));
		using var client = new HttpClient(handler);
		var provider = new OpenRouteServiceProvider(client, new() { ApiKey = "test-key" });
		var error = await Assert.ThrowsAsync<RoutingException>(() => provider.GetRoadRouteAsync(Start, Destination, TestContext.Current.CancellationToken));
		Assert.Equal(failure, error.Failure);
		Assert.DoesNotContain("secret", error.ToString());
	}

	[Theory]
	[InlineData("not-json")]
	[InlineData("{}")]
	[InlineData("{\"type\":\"FeatureCollection\",\"features\":[]}")]
	[InlineData("{\"type\":\"FeatureCollection\",\"features\":[null]}")]
	public async Task MalformedSuccess_IsRejected(string body)
	{
		await AssertInvalidResponse(body);
	}

	[Theory]
	[InlineData("[34.7818,32.0853,12.5]", "[34.7818,92,12.5]")]
	[InlineData("[34.7818,32.0853,12.5]", "[1e309,32.0853,12.5]")]
	[InlineData("[34.7818,32.0853,12.5]", "[34.7818,32.0853,1e309]")]
	[InlineData("4567.8", "null")]
	[InlineData("4567.8", "-1")]
	[InlineData("987.6", "0")]
	[InlineData("987.6", "1e309")]
	[InlineData("\"ascent\":15", "\"ascent\":-1")]
	[InlineData("\"LineString\"", "\"Polygon\"")]
	public async Task InvalidGeometryOrMetrics_IsRejected(string original, string replacement)
	{
		await AssertInvalidResponse(ValidResponse.Replace(original, replacement));
	}

	[Fact]
	public async Task MissingKey_DoesNotSendRequest()
	{
		using var handler = new StubHandler((_, _) => throw new Xunit.Sdk.XunitException("No network call expected."));
		using var client = new HttpClient(handler);
		var provider = new OpenRouteServiceProvider(client, new());
		var error = await Assert.ThrowsAsync<RoutingException>(() => provider.GetRoadRouteAsync(Start, Destination, TestContext.Current.CancellationToken));
		Assert.Equal(RoutingFailure.NotConfigured, error.Failure);
	}

	[Fact]
	public async Task CallerCancellation_IsNotConvertedToProviderFailure()
	{
		using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		using var handler = new StubHandler(async (_, ct) =>
		{
			source.Cancel();
			await Task.Delay(Timeout.Infinite, ct);
			return Response(200, ValidResponse);
		});
		using var client = new HttpClient(handler);
		var provider = new OpenRouteServiceProvider(client, new() { ApiKey = "test-key" });
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetRoadRouteAsync(Start, Destination, source.Token));
	}

	[Fact]
	public async Task TransportTimeout_IsReportedSeparately()
	{
		using var handler = new StubHandler((_, _) => throw new TaskCanceledException());
		using var client = new HttpClient(handler);
		var provider = new OpenRouteServiceProvider(client, new() { ApiKey = "test-key" });
		var error = await Assert.ThrowsAsync<RoutingException>(() => provider.GetRoadRouteAsync(Start, Destination, TestContext.Current.CancellationToken));
		Assert.Equal(RoutingFailure.Timeout, error.Failure);
	}

	private static async Task AssertInvalidResponse(string body)
	{
		using var handler = new StubHandler((_, _) => Task.FromResult(Response(200, body)));
		using var client = new HttpClient(handler);
		var provider = new OpenRouteServiceProvider(client, new() { ApiKey = "test-key" });
		var error = await Assert.ThrowsAsync<RoutingException>(() => provider.GetRoadRouteAsync(Start, Destination, TestContext.Current.CancellationToken));
		Assert.Equal(RoutingFailure.InvalidResponse, error.Failure);
	}

	internal static HttpResponseMessage Response(int status, string body) => new((HttpStatusCode)status)
	{
		Content = new StringContent(body, Encoding.UTF8, "application/json")
	};

	internal sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
	}
}
