using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CyclingRoutes.Tests.Integration;

public class RoadLoopControlCorpusTests
{
    [Fact]
    public async Task EveryPublicControlIsValidAndBoundsArePreservedWithoutProviderCalls()
    {
        using var corpus = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "road-loop-control-v1.json"), TestContext.Current.CancellationToken));
        var root = corpus.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("/api/route-intents/validate", root.GetProperty("requestEndpoint").GetString());
        Assert.Equal(3, root.GetProperty("maxRoutingCallsPerArm").GetInt32());
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(8, cases.Length);
        Assert.Equal(8, cases.Select(c => c.GetProperty("id").GetString()).Distinct().Count());
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(OfflineProviders.Configure));
        using var client = factory.CreateClient();
        foreach (var entry in cases)
        {
            var request = entry.GetProperty("request");
            Assert.Equal("loop", request.GetProperty("shape").GetString());
            Assert.Equal("road", request.GetProperty("profile").GetString());
            using var content = new StringContent(request.GetRawText(), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("/api/route-intents/validate", content, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            foreach (var target in request.EnumerateObject().Where(p => p.Name.StartsWith("target", StringComparison.Ordinal)))
                Assert.True(JsonElement.DeepEquals(target.Value, body.RootElement.GetProperty(target.Name)));
        }
    }
}
