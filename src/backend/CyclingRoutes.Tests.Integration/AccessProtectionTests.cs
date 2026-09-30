using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CyclingRoutes.Tests.Integration;

public class AccessProtectionTests
{
	internal const string Origin = "https://testers.example.test";
	internal const string Password = "test-only-password-not-a-real-secret";
	internal const string InterpretBody = """{"prompt":"A road loop of 20 km","locale":"en","start":{"latitude":32,"longitude":34}}""";
	internal const string InterpretUrl = "/api/route-intents/interpret";

	internal static WebApplicationFactory<Program> Factory(string? mode = null, string? password = Password,
		string? origin = Origin, string environment = "Production") => new WebApplicationFactory<Program>()
		.WithWebHostBuilder(b => b.UseEnvironment(environment).ConfigureAppConfiguration((_, c) =>
			c.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Access:Mode"] = mode, ["Access:Password"] = password, ["Access:PublicOrigin"] = origin,
				["Ai:Gemini:ApiKey"] = "", ["Ai:Gemini:Model"] = "", ["Routing:OpenRouteService:ApiKey"] = ""
			})).ConfigureTestServices(OfflineProviders.Configure));

	internal static HttpClient Client(WebApplicationFactory<Program> factory, bool authenticated = true)
	{
		var client = factory.CreateClient(new() { BaseAddress = new("http://localhost"), AllowAutoRedirect = false });
		if (authenticated) client.DefaultRequestHeaders.Authorization = Credentials();
		return client;
	}

	internal static AuthenticationHeaderValue Credentials(string username = "tester", string password = Password) =>
		new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));

	internal static HttpRequestMessage Post(string? origin = Origin, string body = InterpretBody)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, InterpretUrl)
		{ Content = new StringContent(body, Encoding.UTF8, "application/json") };
		if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
		return request;
	}

	[Theory]
	[InlineData("")]
	[InlineData("IRouteIntentInterpreter")]
	[InlineData("IRouteSearchAdvisor")]
	[InlineData("IRoutingProvider")]
	public async Task AccessFactory_BlocksNamedProviderHttpClients(string name)
	{
		using var factory = Factory();
		using var client = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(name);
		// Loopback discard port: even a broken test guard cannot contact a provider.
		var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			client.GetAsync("http://127.0.0.1:9", TestContext.Current.CancellationToken));
		Assert.Equal("Outbound HTTP is forbidden in offline tests.", error.Message);
	}

	[Theory]
	[InlineData("/")] [InlineData("/index.html")] [InlineData("/assets/app.js")]
	[InlineData("/api/routes/plan")] [InlineData("/api/unknown")] [InlineData("/health/")]
	public async Task AnonymousRequests_AreChallenged(string path)
	{
		using var factory = Factory();
		using var client = Client(factory, false);
		using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		Assert.Equal("Basic", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
		Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
		Assert.Equal("access_unauthorized", (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString());
	}

	[Theory]
	[InlineData("GET", 200)] [InlineData("HEAD", 200)] [InlineData("POST", 401)]
	[InlineData("OPTIONS", 401)] [InlineData("DELETE", 401)]
	public async Task OnlyReadHealth_IsAnonymous(string method, int expected)
	{
		using var factory = Factory();
		using var client = Client(factory, false);
		using var request = new HttpRequestMessage(new(method), "/health");
		using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
		Assert.Equal(expected, (int)response.StatusCode);
		Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
		Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
		Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
		Assert.Equal("geolocation=(self), camera=(), microphone=()", Assert.Single(response.Headers.GetValues("Permissions-Policy")));
	}

	[Theory]
	[InlineData(null, null, Origin)] [InlineData(null, "too-short", Origin)]
	[InlineData(null, Password, null)] [InlineData(null, Password, "http://testers.example.test")]
	[InlineData(null, Password, "https://testers.example.test/path")]
	[InlineData(null, Password, "https://testers.example.test/")]
	[InlineData(null, Password, "https://tester:password@testers.example.test")]
	[InlineData(null, Password, "https://testers.example.test?query=1")]
	[InlineData(null, Password, "https://testers.example.test#fragment")]
	[InlineData("Public", Password, Origin)]
	public void InvalidProductionConfiguration_RejectsStartup(string? mode, string? password, string? origin)
	{
		using var factory = Factory(mode, password, origin);
		var error = Assert.Throws<OptionsValidationException>(() => Client(factory));
		Assert.DoesNotContain(Password, error.Message);
	}

	[Theory]
	[InlineData("Production", "Local")] [InlineData("Development", null)] [InlineData("Testing", null)]
	public async Task ExplicitLocalOrLocalEnvironment_AllowsAnonymousApi(string environment, string? mode)
	{
		using var factory = Factory(mode, null, null, environment);
		using var client = Client(factory, false);
		using var request = Post(null);
		using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
	}

	[Theory]
	[InlineData("Bearer token")] [InlineData("Basic not-base64!")]
	[InlineData("Basic dGVzdGVy")] [InlineData("Basic ")] [InlineData("Basic /w==")]
	public async Task MalformedCredentials_AreChallenged(string authorization)
	{
		using var factory = Factory();
		using var client = Client(factory, false);
		client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization);
		using var response = await client.GetAsync("/api/unknown", TestContext.Current.CancellationToken);
		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Theory]
	[InlineData("Tester", Password)] [InlineData("other", Password)] [InlineData("tester", "wrong")]
	public async Task WrongCredentials_AreChallenged(string username, string password)
	{
		using var factory = Factory();
		using var client = Client(factory);
		client.DefaultRequestHeaders.Authorization = Credentials(username, password);
		using var response = await client.GetAsync("/api/unknown", TestContext.Current.CancellationToken);
		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task OversizedAndDuplicateAuthorization_AreChallenged()
	{
		using var factory = Factory();
		using var client = Client(factory, false);
		foreach (var values in new[] { new[] { "Basic " + new string('A', 1024) }, new[] { Credentials().ToString(), Credentials().ToString() } })
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, "/api/unknown");
			request.Headers.TryAddWithoutValidation("Authorization", values);
			using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
			Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		}
	}

	[Theory]
	[InlineData(null)] [InlineData("null")] [InlineData("https://evil.example.test")]
	[InlineData(Origin + "/")] [InlineData("http://testers.example.test")]
	[InlineData(Origin + ", https://evil.example.test")]
	public async Task UnsafeApi_RejectsMissingOrNonExactOriginEvenWithCachedCredentials(string? origin)
	{
		using var factory = Factory();
		using var client = Client(factory);
		using var request = Post(origin);
		request.Headers.Host = "evil.example.test";
		request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "evil.example.test");
		request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
		using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
		Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
		Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
		Assert.Equal("access_origin_forbidden", (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString());
	}

	[Fact]
	public async Task CrossSiteFetchMetadata_RejectsEvenMatchingOrigin()
	{
		using var factory = Factory();
		using var client = Client(factory);
		using var request = Post();
		request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
		using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
		Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
	}

	[Fact]
	public async Task DuplicateOrigin_IsRejected()
	{
		using var factory = Factory();
		using var client = Client(factory);
		using var request = Post();
		request.Headers.TryAddWithoutValidation("Origin", Origin);
		using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
		Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
	}

	[Theory]
	[InlineData(InterpretBody, 503)] [InlineData("{", 400)]
	[InlineData("{\"prompt\":42,\"locale\":\"en\"}", 400)]
	public async Task SameOriginApi_PreservesStrictJsonAndNoKeyFailure(string body, int status)
	{
		using var factory = Factory();
		using var client = Client(factory);
		using var request = Post(body: body);
		request.Headers.Host = "internal-proxy";
		request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");
		using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
		Assert.Equal(status, (int)response.StatusCode);
		if (status == 503) Assert.Contains("ai_not_configured", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
		Assert.Null(response.Headers.Location);
	}

	[Theory]
	[InlineData("GET", "/api/unknown")]
	[InlineData("POST", "/api/unknown")]
	[InlineData("DELETE", "/API/unknown.json")]
	[InlineData("POST", "/api")]
	public async Task UnknownApi_Is404ForEveryMethodAndNeverSpa(string method, string path)
	{
		using var factory = Factory();
		using var client = Client(factory);
		using var request = new HttpRequestMessage(new(method), path);
		request.Headers.TryAddWithoutValidation("Origin", Origin);
		using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
		Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
	}
}
