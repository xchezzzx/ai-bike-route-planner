using System.Text.Json;
using System.Text.Json.Serialization;
using CyclingRoutes.Application.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CyclingRoutes.Tests.Integration;

public class RouteRefinementQualificationTests
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
	{ Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) } };
	private sealed record AdvisorCase(string Id, RouteSearchContext Context);

	[Fact]
	public async Task CorpusIsValidAndLiveAdvisorRunsOnlyWithExplicitOptIn()
	{
		using var corpus = JsonDocument.Parse(await File.ReadAllTextAsync(System.IO.Path.Combine(AppContext.BaseDirectory, "route-refinement-v1.json"), TestContext.Current.CancellationToken));
		Assert.Equal("route-search-v1", corpus.RootElement.GetProperty("contractVersion").GetString());
		var cases = JsonSerializer.Deserialize<AdvisorCase[]>(corpus.RootElement.GetProperty("advisorCases"), Json)!;
		Assert.Equal(6, cases.Length);
		Assert.Equal(6, cases.Select(x => x.Id).Distinct().Count());
		Assert.All(cases, c => { Assert.InRange(c.Context.InitialLengthMeters, 1000, 100000); Assert.InRange(c.Context.Observations.Count, 1, 2); });
		if (Environment.GetEnvironmentVariable("CYCLING_LIVE_ADVISOR") != "1") return;
		Assert.NotEqual("true", Environment.GetEnvironmentVariable("CI"));
		using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development"));
		var advisor = app.Services.GetRequiredService<IRouteSearchAdvisor>();
		var results = new List<object>();
		var failed = false;
		var stop = false;
		var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts"));
		Directory.CreateDirectory(directory);
		var output = System.IO.Path.Combine(directory, $"advisor-qualification-{DateTime.UtcNow:yyyyMMddTHHmmss}-{Guid.NewGuid():N}.json");
		foreach (var c in cases)
		{
			if (stop) { results.Add(new { c.Id, status = "unrun" }); continue; }
			if (results.Count > 0) await Task.Delay(5000, TestContext.Current.CancellationToken);
			var timer = System.Diagnostics.Stopwatch.StartNew();
			try
			{
				var advice = await advisor.AdviseAsync(c.Context, TestContext.Current.CancellationToken);
				var valid = RouteSearchProposalPolicy.IsValid(advice, c.Context);
				failed |= !valid;
				results.Add(new { c.Id, status = valid ? "passed" : "invalid", latencyMs = timer.ElapsedMilliseconds, advice });
			}
			catch (RouteSearchAdvisorException error)
			{
				failed = true;
				stop = error.Failure is RouteSearchAdvisorFailure.Quota or RouteSearchAdvisorFailure.Authentication or RouteSearchAdvisorFailure.NotConfigured;
				results.Add(new { c.Id, status = "error", latencyMs = timer.ElapsedMilliseconds, failure = error.Failure });
			}
			finally { await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { contractVersion = "route-search-v1", results }, Json), CancellationToken.None); }
		}
		await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { contractVersion = "route-search-v1", results }, Json), CancellationToken.None);
		TestContext.Current.TestOutputHelper!.WriteLine($"Advisor qualification report: {output}");
		Assert.False(failed, "Live advisor qualification failed; see sanitized report. No automatic retries.");
	}
}
