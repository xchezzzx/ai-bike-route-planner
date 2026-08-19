using Microsoft.AspNetCore.Mvc.Testing;

namespace CyclingRoutes.Tests.Integration;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly WebApplicationFactory<Program> _factory;

	public HealthEndpointTests(WebApplicationFactory<Program> factory)
	{
		_factory = factory;
	}

	[Fact]
	public async Task GetHealth_ReturnsOkAndHealthyBody()
	{
		// Arrange
		using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions()
		{
			BaseAddress = new Uri("https://localhost")
		});

		// Act
		using var response = await client.GetAsync("/health");
		var content = await response.Content.ReadAsStringAsync();

		// Assert
		Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("Healthy", content);
	}
}