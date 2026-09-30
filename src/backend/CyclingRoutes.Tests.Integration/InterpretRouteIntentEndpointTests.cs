using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CyclingRoutes.Application.Interpretation;
using CyclingRoutes.Contracts.RoutePlanning;
using CyclingRoutes.Infrastructure.Interpretation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CyclingRoutes.Tests.Integration;

public class InterpretRouteIntentEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private const string Url = "/api/route-intents/interpret";
	private readonly WebApplicationFactory<Program> _factory;
	public InterpretRouteIntentEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;
	private static readonly RouteIntentExtraction Complete = new("loop", "road", null, 20000, null, []);
	private static InterpretRouteIntentRequest Request() => new() { Prompt = "A road loop of 20 km", Locale = "en", Start = new() { Latitude = 32, Longitude = 34 } };
	private WebApplicationFactory<Program> Factory(Stub? stub = null, string environment = "Production") => _factory.WithWebHostBuilder(b =>
		b.UseEnvironment(environment).UseSetting("Access:Mode", "Local").ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
		{ ["Ai:Gemini:ApiKey"] = "", ["Ai:Gemini:Model"] = "" }))
		.ConfigureTestServices(s => { OfflineProviders.Configure(s); if (stub is not null) s.AddSingleton<IRouteIntentInterpreter>(stub); }));
	private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new() { BaseAddress = new("https://localhost") });

	[Theory]
	[InlineData("Development")]
	[InlineData("Production")]
	public async Task CompleteRequest_ReturnsParametersNotGeometry(string environment)
	{
		var stub = new Stub(); using var factory = Factory(stub, environment); using var client = Client(factory);
		using var response = await client.PostAsJsonAsync(Url, Request(), TestContext.Current.CancellationToken);
		Assert.Equal(200, (int)response.StatusCode);
		var result = (await response.Content.ReadFromJsonAsync<InterpretRouteIntentResponse>(TestContext.Current.CancellationToken))!;
		Assert.Equal("ready", result.Status); Assert.Equal(20000, result.Intent!.TargetDistanceMeters);
		Assert.Equal(32, result.Intent.Start.Latitude); Assert.Equal("road", result.Intent.Profile);
		Assert.Equal(new[] { "elevation_balanced" }, result.Assumptions); Assert.Empty(result.Clarifications);
		var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		Assert.DoesNotContain("geometry", text); Assert.DoesNotContain("gpx", text); Assert.Equal(1, stub.Calls);
	}

	[Theory]
	[InlineData("missing", "needsClarification")]
	[InlineData("gravel", "unsupported")]
	[InlineData("stop", "unsupported")]
	public async Task UnresolvedRequests_ReturnExplicitStates(string kind, string status)
	{
		var stub = new Stub(kind == "gravel" ? Complete with { Profile = "gravel" } : kind == "stop" ? Complete with { Issues = [new("prompt", "unsupported_preference")] } : Complete);
		using var factory = Factory(stub); using var client = Client(factory);
		using var response = await client.PostAsJsonAsync(Url, Request() with { Start = kind == "missing" ? null : Request().Start }, TestContext.Current.CancellationToken);
		Assert.Equal(200, (int)response.StatusCode);
		var body = (await response.Content.ReadFromJsonAsync<InterpretRouteIntentResponse>(TestContext.Current.CancellationToken))!;
		Assert.Equal(status, body.Status);
		if (kind != "gravel") Assert.Null(body.Intent);
	}

	[Theory]
	[InlineData("en", "road, 20 km", "loop")]
	[InlineData("ru", "шоссе, 20 км", "loop")]
	[InlineData("he", "כביש, 20 קילומטרים", "loop")]
	[InlineData("en", "road", "pointToPoint")]
	[InlineData("ru", "шоссе", "pointToPoint")]
	[InlineData("he", "כביש", "pointToPoint")]
	public async Task SelectedShape_FillsMissingExtractionWithoutRepeatingShapeInPrompt(string locale, string prompt, string shape)
	{
		var request = Request() with
		{
			Locale = locale, Prompt = prompt,
			Destination = shape == "pointToPoint" ? new() { Latitude = 32.2, Longitude = 34.8 } : null
		};
		var stub = new Stub(Complete with { Shape = null, TargetDistanceMeters = shape == "loop" ? 20000 : null });
		using var factory = Factory(stub); using var client = Client(factory);
		using var response = await PostWithShape(client, request, shape);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = (await response.Content.ReadFromJsonAsync<InterpretRouteIntentResponse>(TestContext.Current.CancellationToken))!;
		Assert.Equal("ready", body.Status);
		Assert.Equal(shape, body.Draft.Shape); Assert.Equal(shape, body.Intent!.Shape);
		Assert.Equal(request.Start, body.Draft.Start);
		Assert.Equal(request.Destination, body.Draft.Destination);
		Assert.Equal(request.Start!.Latitude, body.Intent.Start.Latitude);
		Assert.Equal(request.Start.Longitude, body.Intent.Start.Longitude);
		Assert.Equal(request.Destination?.Latitude, body.Intent.Destination?.Latitude);
		Assert.Equal(request.Destination?.Longitude, body.Intent.Destination?.Longitude);
		Assert.Empty(body.Clarifications); Assert.Empty(body.Limitations);
		Assert.Equal((prompt, locale), stub.Input); Assert.Equal(1, stub.Calls);
	}

	[Theory]
	[InlineData("en", "loop", "pointToPoint")]
	[InlineData("ru", "loop", "pointToPoint")]
	[InlineData("he", "loop", "pointToPoint")]
	[InlineData("en", "pointToPoint", "loop")]
	[InlineData("ru", "pointToPoint", "loop")]
	[InlineData("he", "pointToPoint", "loop")]
	public async Task SelectedShape_ConflictProducesOnlyLocalizedShapeQuestion(string locale, string selected, string extracted)
	{
		var request = Request() with
		{
			Locale = locale,
			Destination = selected == "pointToPoint" ? new() { Latitude = 32.2, Longitude = 34.8 } : null
		};
		var stub = new Stub(Complete with { Shape = extracted, TargetDistanceMeters = null });
		using var factory = Factory(stub); using var client = Client(factory);
		using var response = await PostWithShape(client, request, selected);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = (await response.Content.ReadFromJsonAsync<InterpretRouteIntentResponse>(TestContext.Current.CancellationToken))!;
		Assert.Equal("needsClarification", body.Status); Assert.Null(body.Intent);
		Assert.Equal(extracted, body.Draft.Shape);
		var question = Assert.Single(body.Clarifications);
		Assert.Equal("shape", question.Field); Assert.Equal("route_shape_conflict", question.Code);
		Assert.Contains(locale switch { "ru" => "Тип маршрута", "he" => "סוג מסלול", _ => "Route shape" }, question.Message);
		Assert.Contains(locale switch { "ru" => "противоречит запросу", "he" => "סותר את הבקשה", _ => "conflicts with the prompt" }, question.Message);
		Assert.Empty(body.Limitations); Assert.Equal(1, stub.Calls);
	}

	[Theory]
	[InlineData("en", "loop", "needsClarification")]
	[InlineData("ru", "loop", "needsClarification")]
	[InlineData("he", "loop", "needsClarification")]
	[InlineData("en", "pointToPoint", "ready")]
	[InlineData("ru", "pointToPoint", "ready")]
	[InlineData("he", "pointToPoint", "ready")]
	public async Task SelectedShape_TargetsAreRequiredOnlyForLoops(string locale, string shape, string status)
	{
		var request = Request() with
		{
			Locale = locale, Prompt = "road",
			Destination = shape == "pointToPoint" ? new() { Latitude = 32.2, Longitude = 34.8 } : null
		};
		using var factory = Factory(new Stub(Complete with { Shape = null, TargetDistanceMeters = null })); using var client = Client(factory);
		using var response = await PostWithShape(client, request, shape);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = (await response.Content.ReadFromJsonAsync<InterpretRouteIntentResponse>(TestContext.Current.CancellationToken))!;
		Assert.Equal(status, body.Status);
		Assert.Equal(shape, body.Draft.Shape);
		if (shape == "loop")
		{
			Assert.Null(body.Intent);
			var question = Assert.Single(body.Clarifications);
			Assert.Equal("targetDistanceMeters", question.Field); Assert.Equal("target_required", question.Code);
		}
		else
		{
			Assert.Empty(body.Clarifications); Assert.NotNull(body.Intent);
			Assert.Null(body.Intent.TargetDistanceMeters); Assert.Null(body.Intent.TargetDurationSeconds);
		}
	}

	[Theory]
	[InlineData("shape", "ambiguous")]
	[InlineData("shape", "invalid_value")]
	[InlineData("start", "location_requires_map_selection")]
	[InlineData("destination", "location_requires_map_selection")]
	[InlineData("prompt", "unsupported_preference")]
	[InlineData("shape", "unsupported_preference")]
	public async Task SelectedShape_DoesNotSwallowExtractionIssues(string field, string code)
	{
		using var factory = Factory(new Stub(Complete with { Shape = null, Issues = [new(field, code)] })); using var client = Client(factory);
		using var response = await PostWithShape(client, Request(), "loop");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = (await response.Content.ReadFromJsonAsync<InterpretRouteIntentResponse>(TestContext.Current.CancellationToken))!;
		Assert.Null(body.Intent);
		Assert.Equal("loop", body.Draft.Shape);
		if (code == "unsupported_preference")
		{
			Assert.Equal("unsupported", body.Status); Assert.Empty(body.Clarifications);
			Assert.Equal(new[] { "unsupported_preference" }, body.Limitations);
		}
		else
		{
			Assert.Equal("needsClarification", body.Status);
			var question = Assert.Single(body.Clarifications);
			Assert.Equal(field, question.Field); Assert.Equal(code, question.Code);
		}
	}

	[Theory]
	[InlineData("Development")]
	[InlineData("Production")]
	public async Task SelectedShape_InvalidValuesReturnValidationProblemBeforeInterpreter(string environment)
	{
		var stub = new Stub(); using var factory = Factory(stub, environment); using var client = Client(factory);
		foreach (var shape in new[] { "", " ", "Loop", "pointtopoint", "pointToPoint ", " loop", "triangle", "A-to-B" })
		{
			using var response = await PostWithShape(client, Request(), shape);
			Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
			using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
			Assert.True(json.RootElement.TryGetProperty("errors", out var errors), "Invalid selected shape must be an envelope validation error.");
			Assert.Equal("invalid_value", errors.GetProperty("shape")[0].GetString());
		}
		Assert.Equal(0, stub.Calls);
	}

	[Theory]
	[InlineData("loop")]
	[InlineData("pointToPoint")]
	public async Task SelectedShape_MatchingExtractionIsReady(string shape)
	{
		using var factory = Factory(new Stub(Complete with { Shape = shape })); using var client = Client(factory);
		using var response = await PostWithShape(client, Request() with
		{
			Destination = shape == "pointToPoint" ? new() { Latitude = 32.2, Longitude = 34.8 } : null
		}, shape);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = (await response.Content.ReadFromJsonAsync<InterpretRouteIntentResponse>(TestContext.Current.CancellationToken))!;
		Assert.Equal("ready", body.Status); Assert.Equal(shape, body.Intent!.Shape); Assert.Empty(body.Clarifications);
	}

	[Theory]
	[InlineData(false, "loop")]
	[InlineData(true, "loop")]
	[InlineData(false, "pointToPoint")]
	[InlineData(true, "pointToPoint")]
	[InlineData(false, null)]
	[InlineData(true, null)]
	public async Task LegacyAbsentOrNullShape_KeepsExtractionAndMissingShapeBehavior(bool explicitNull, string? extractedShape)
	{
		var request = Request() with
		{
			Destination = extractedShape == "pointToPoint" ? new() { Latitude = 32.2, Longitude = 34.8 } : null
		};
		using var factory = Factory(new Stub(Complete with { Shape = extractedShape, TargetDistanceMeters = null })); using var client = Client(factory);
		var envelope = JsonSerializer.SerializeToNode(request, JsonSerializerOptions.Web)!.AsObject();
		if (explicitNull) envelope["shape"] = null; else envelope.Remove("shape");
		using var response = await client.PostAsync(Url, new StringContent(envelope.ToJsonString(), Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = (await response.Content.ReadFromJsonAsync<InterpretRouteIntentResponse>(TestContext.Current.CancellationToken))!;
		Assert.Equal(extractedShape, body.Draft.Shape);
		if (extractedShape == "pointToPoint")
		{
			Assert.Equal("ready", body.Status); Assert.Equal(extractedShape, body.Intent!.Shape); Assert.Empty(body.Clarifications);
		}
		else
		{
			Assert.Equal("needsClarification", body.Status); Assert.Null(body.Intent);
			Assert.Equal(extractedShape is null ? 2 : 1, body.Clarifications.Count);
			Assert.Contains(body.Clarifications, x => x.Field == "targetDistanceMeters" && x.Code == "target_required");
			if (extractedShape is null) Assert.Contains(body.Clarifications, x => x.Field == "shape" && x.Code == "required");
		}
	}

	[Theory]
	[InlineData("missing", "required")]
	[InlineData("equal", "must_differ_from_start")]
	[InlineData("loop", "destination_not_allowed")]
	public async Task SelectedShape_PreservesDestinationInvariants(string scenario, string code)
	{
		using var factory = Factory(new Stub(Complete with { Shape = null })); using var client = Client(factory);
		using var response = await PostWithShape(client, Request() with
		{
			Destination = scenario == "missing" ? null : Request().Start
		}, scenario == "loop" ? "loop" : "pointToPoint");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = (await response.Content.ReadFromJsonAsync<InterpretRouteIntentResponse>(TestContext.Current.CancellationToken))!;
		Assert.Equal("needsClarification", body.Status); Assert.Null(body.Intent);
		var question = Assert.Single(body.Clarifications);
		Assert.Equal("destination", question.Field); Assert.Equal(code, question.Code);
	}

	[Theory]
	[InlineData("start", "location_requires_map_selection")]
	[InlineData("destination", "location_requires_map_selection")]
	[InlineData("prompt", "unsupported_preference")]
	[InlineData("shape", "unsupported_preference")]
	public async Task SelectedShape_ConflictPreservesIndependentExtractionIssues(string field, string code)
	{
		using var factory = Factory(new Stub(Complete with { TargetDistanceMeters = null, Issues = [new(field, code)] })); using var client = Client(factory);
		using var response = await PostWithShape(client, Request() with { Destination = new() { Latitude = 32.2, Longitude = 34.8 } }, "pointToPoint");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var body = (await response.Content.ReadFromJsonAsync<InterpretRouteIntentResponse>(TestContext.Current.CancellationToken))!;
		Assert.Equal("needsClarification", body.Status); Assert.Null(body.Intent);
		Assert.Contains(body.Clarifications, x => x.Field == "shape" && x.Code == "route_shape_conflict");
		if (code == "unsupported_preference")
		{
			Assert.Single(body.Clarifications); Assert.Equal(new[] { "unsupported_preference" }, body.Limitations);
		}
		else
		{
			Assert.Equal(2, body.Clarifications.Count);
			Assert.Contains(body.Clarifications, x => x.Field == field && x.Code == code);
		}
	}

	[Theory]
	[InlineData("Development")]
	[InlineData("Production")]
	public async Task SelectedShape_MalformedTokensAndDuplicatesReturn400BeforeInterpreter(string environment)
	{
		var stub = new Stub(); using var factory = Factory(stub, environment); using var client = Client(factory);
		var envelope = JsonSerializer.SerializeToNode(Request(), JsonSerializerOptions.Web)!.AsObject();
		envelope.Remove("shape");
		var valid = envelope.ToJsonString();
		foreach (var token in new[] { "1", "true", "[]", "{}", "\"loop\",\"shape\":\"pointToPoint\"" })
		{
			var body = valid[..^1] + ",\"shape\":" + token + "}";
			using var response = await client.PostAsync(Url, new StringContent(body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
			Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		}
		Assert.Equal(0, stub.Calls);
	}

	[Theory]
	[InlineData(InterpretationFailure.NotConfigured, 503, "ai_not_configured")]
	[InlineData(InterpretationFailure.CredentialsRejected, 503, "ai_credentials_rejected")]
	[InlineData(InterpretationFailure.RateLimited, 503, "ai_rate_limited")]
	[InlineData(InterpretationFailure.Unavailable, 503, "ai_unavailable")]
	[InlineData(InterpretationFailure.Timeout, 504, "ai_timeout")]
	[InlineData(InterpretationFailure.RequestRejected, 422, "ai_request_rejected")]
	[InlineData(InterpretationFailure.InvalidResponse, 502, "ai_invalid_response")]
	public async Task ProviderFailure_MapsToSafeProblem(InterpretationFailure failure, int status, string code)
	{
		using var factory = Factory(new Stub(failure: failure)); using var client = Client(factory);
		using var response = await client.PostAsJsonAsync(Url, Request(), TestContext.Current.CancellationToken);
		Assert.Equal(status, (int)response.StatusCode);
		using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
		Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
		Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
		Assert.False(json.RootElement.TryGetProperty("intent", out _));
	}

	[Fact]
	public async Task MissingConfig_DoesNotPreventHealth()
	{
		using var factory = Factory(); using var client = Client(factory);
		Assert.True(string.IsNullOrEmpty(factory.Services.GetRequiredService<GeminiOptions>().ApiKey), "Gemini credentials must be disabled.");
		Assert.True(string.IsNullOrEmpty(factory.Services.GetRequiredService<GeminiOptions>().Model), "Gemini model must be disabled.");
		Assert.Equal("Healthy", await client.GetStringAsync("/health", TestContext.Current.CancellationToken));
		using var response = await client.PostAsJsonAsync(Url, Request(), TestContext.Current.CancellationToken);
		Assert.Equal(503, (int)response.StatusCode);
	}

	[Theory]
	[InlineData("Development")]
	[InlineData("Production")]
	public async Task InvalidBodies_Return400BeforeInterpreter(string environment)
	{
		var stub = new Stub(); using var factory = Factory(stub, environment); using var client = Client(factory);
		var valid = JsonSerializer.Serialize(Request(), JsonSerializerOptions.Web);
		foreach (var body in new[] { "{", "{}", "null", valid.Replace("\"latitude\":32", "\"latitude\":\"32\""), valid[..^1] + ",\"extra\":true}", valid.Replace("\"en\"", "\"xx\"") })
		{
			using var response = await client.PostAsync(Url, new StringContent(body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
			Assert.Equal(400, (int)response.StatusCode);
		}
		Assert.Equal(0, stub.Calls);
	}

	[Theory]
	[InlineData(65536, false, 200)]
	[InlineData(65537, false, 413)]
	[InlineData(65536, true, 200)]
	[InlineData(65537, true, 413)]
	public async Task BodyLimit_CoversMissingContentLength(int length, bool knownLength, int expected)
	{
		var stub = new Stub(); using var factory = Factory(stub); using var client = Client(factory);
		var body = JsonSerializer.Serialize(Request(), JsonSerializerOptions.Web).PadRight(length);
		using var content = knownLength ? (HttpContent)new StringContent(body, Encoding.UTF8, "application/json") : new StreamContent(new Unseekable(Encoding.UTF8.GetBytes(body)));
		content.Headers.ContentType = new("application/json");
		using var response = await client.PostAsync(Url, content, TestContext.Current.CancellationToken);
		Assert.Equal(expected, (int)response.StatusCode); Assert.Equal(expected == 200 ? 1 : 0, stub.Calls);
	}

	[Theory]
	[InlineData(false, 4000, 200)]
	[InlineData(true, 4000, 200)]
	[InlineData(false, 4001, 400)]
	[InlineData(true, 4001, 400)]
	public async Task UnicodePromptLimit_IsIndependentOfEncoding(bool escaped, int length, int expected)
	{
		using var factory = Factory(new Stub()); using var client = Client(factory);
		var body = JsonSerializer.Serialize(Request() with { Prompt = new string('ש', length), Locale = "he" },
			new JsonSerializerOptions(JsonSerializerOptions.Web) { Encoder = escaped ? JavaScriptEncoder.Default : JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
		using var response = await client.PostAsync(Url, new StringContent(body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
		Assert.Equal(expected, (int)response.StatusCode);
	}

	[Theory]
	[InlineData("text/plain", 415)]
	[InlineData("application/json; charset=utf-16", 415)]
	[InlineData("application/vnd.route+json", 200)]
	public async Task MediaType_IsValidated(string mediaType, int expected)
	{
		var stub = new Stub(); using var factory = Factory(stub); using var client = Client(factory);
		using var content = new StringContent(JsonSerializer.Serialize(Request(), JsonSerializerOptions.Web));
		content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(mediaType);
		using var response = await client.PostAsync(Url, content, TestContext.Current.CancellationToken);
		Assert.Equal(expected, (int)response.StatusCode); Assert.Equal(expected == 200 ? 1 : 0, stub.Calls);
	}

	[Fact]
	public async Task OpenApi_DescribesRequestAndFailures()
	{
		using var factory = Factory(new Stub(), "Development"); using var client = Client(factory);
		using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken));
		var operation = json.RootElement.GetProperty("paths").GetProperty(Url).GetProperty("post");
		Assert.True(operation.TryGetProperty("requestBody", out _));
		Assert.True(operation.GetProperty("responses").TryGetProperty("413", out _));
	}

	[Fact]
	public async Task RequestCancellation_ReachesInterpreter()
	{
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var factory = Factory(new Stub(onCall: async ct =>
		{
			started.SetResult();
			try { await Task.Delay(Timeout.Infinite, ct); }
			catch (OperationCanceledException) { cancelled.SetResult(); throw; }
		}));
		using var client = Client(factory); using var source = new CancellationTokenSource();
		var call = client.PostAsJsonAsync(Url, Request(), source.Token);
		await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
		source.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
		await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
	}

	private sealed class Unseekable(byte[] bytes) : MemoryStream(bytes) { public override bool CanSeek => false; }
	private static Task<HttpResponseMessage> PostWithShape(HttpClient client, InterpretRouteIntentRequest request, string? shape)
	{
		var body = JsonSerializer.SerializeToNode(request, JsonSerializerOptions.Web)!.AsObject();
		body["shape"] = shape;
		return client.PostAsync(Url, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
	}
	private sealed class Stub(RouteIntentExtraction? value = null, InterpretationFailure? failure = null, Func<CancellationToken, Task>? onCall = null) : IRouteIntentInterpreter
	{
		public int Calls { get; private set; }
		public (string, string) Input { get; private set; }
		public async Task<RouteIntentExtraction> InterpretAsync(string prompt, string locale, CancellationToken cancellationToken)
		{
			Calls++; Input = (prompt, locale); if (onCall is not null) await onCall(cancellationToken);
			if (failure is { } f) throw new InterpretationException(f);
			return value ?? Complete;
		}
	}
}
