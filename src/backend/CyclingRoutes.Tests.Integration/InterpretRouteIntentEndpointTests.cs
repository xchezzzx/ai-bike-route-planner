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
	private sealed class Stub(RouteIntentExtraction? value = null, InterpretationFailure? failure = null, Func<CancellationToken, Task>? onCall = null) : IRouteIntentInterpreter
	{
		public int Calls { get; private set; }
		public async Task<RouteIntentExtraction> InterpretAsync(string prompt, string locale, CancellationToken cancellationToken)
		{
			Calls++; if (onCall is not null) await onCall(cancellationToken);
			if (failure is { } f) throw new InterpretationException(f);
			return value ?? Complete;
		}
	}
}
