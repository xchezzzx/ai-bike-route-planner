using CyclingRoutes.Application.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CyclingRoutes.Tests.Integration;

public class GraphHopperConfigurationTests
{
	[Fact]
	public void ExplicitGraphHopperSelection_UsesLocalAdapter()
	{
		using var factory = Factory("GraphHopper", "http://127.0.0.1:8989/");
		Assert.Equal("GraphHopperProvider", factory.Services.GetRequiredService<IRoutingProvider>().GetType().Name);
	}

	[Fact]
	public void DefaultSelection_PreservesOpenRouteService()
	{
		using var factory = Factory(null, null);
		Assert.Equal("OpenRouteServiceProvider", factory.Services.GetRequiredService<IRoutingProvider>().GetType().Name);
	}

	[Theory]
	[InlineData("Unknown", "http://127.0.0.1:8989/", "road")]
	[InlineData("GraphHopper", "file:///data/", "road")]
	[InlineData("GraphHopper", "https://user:secret@localhost/", "road")]
	[InlineData("GraphHopper", "http://localhost/?key=secret", "road")]
	[InlineData("GraphHopper", "http://localhost/#part", "road")]
	[InlineData("GraphHopper", "http://localhost/route", "road")]
	[InlineData("GraphHopper", "http://localhost/", "")]
	[InlineData("GraphHopper", "http://localhost/", "road?key=secret")]
	public void InvalidSelection_FailsStartupRatherThanFallingBack(string provider, string url, string profile)
	{
		using var factory = Factory(provider, url, profile);
		var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
		Assert.Contains("Routing", error.Message);
		Assert.DoesNotContain("secret", error.ToString());
	}

	private static WebApplicationFactory<Program> Factory(string? provider, string? url, string profile = "road")
	{
		var settings = new Dictionary<string, string?> { ["Access:Mode"] = "Local", ["Routing:GraphHopper:Profile"] = profile };
		if (provider is not null) settings["Routing:Provider"] = provider;
		if (url is not null) settings["Routing:GraphHopper:BaseUrl"] = url;
		return new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
			config.AddInMemoryCollection(settings)));
	}
}
