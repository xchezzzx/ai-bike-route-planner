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

	[Theory]
	[InlineData("before")] [InlineData("pacing")] [InlineData("call")]
	public async Task InterruptedQualificationKeepsEveryCase(string stage)
	{
		using var corpus = JsonDocument.Parse(await File.ReadAllTextAsync(System.IO.Path.Combine(AppContext.BaseDirectory, "route-refinement-v1.json"), TestContext.Current.CancellationToken));
		var cases = JsonSerializer.Deserialize<AdvisorCase[]>(corpus.RootElement.GetProperty("advisorCases"), Json)!;
		using var caller = new CancellationTokenSource();
		if (stage == "before") caller.Cancel();
		var calls = 0;
		var advisor = new StubAdvisor((_, ct) =>
		{
			calls++;
			if (stage == "call" && calls == 2) { caller.Cancel(); ct.ThrowIfCancellationRequested(); }
			return Task.FromResult(new RouteSearchAdvice(RouteSearchAction.Stop, null, null, RouteSearchReason.Stop));
		});
		string? saved = null;
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunCasesAsync(cases, advisor,
			json => { saved = json; return Task.CompletedTask; },
			ct => { if (stage == "pacing") caller.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }, caller.Token));
		using var report = JsonDocument.Parse(saved!);
		var results = report.RootElement.GetProperty("results").EnumerateArray().ToArray();
		Assert.Equal(6, results.Length);
		Assert.Equal("interrupted", report.RootElement.GetProperty("status").GetString());
		Assert.Equal(stage == "before" ? 6 : stage == "call" ? 4 : 5, results.Count(x => x.GetProperty("status").GetString() == "unrun"));
		if (stage == "call") Assert.Equal("interrupted", results[1].GetProperty("status").GetString());
	}

	private sealed class StubAdvisor(Func<RouteSearchContext, CancellationToken, Task<RouteSearchAdvice>> run) : IRouteSearchAdvisor
	{
		public Task<RouteSearchAdvice> AdviseAsync(RouteSearchContext context, CancellationToken cancellationToken) => run(context, cancellationToken);
	}

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
		var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts"));
		Directory.CreateDirectory(directory);
		var output = System.IO.Path.Combine(directory, $"advisor-qualification-{DateTime.UtcNow:yyyyMMddTHHmmss}-{Guid.NewGuid():N}.json");
		TestContext.Current.TestOutputHelper!.WriteLine($"Advisor qualification report: {output}");
		var passed = await RunCasesAsync(cases, advisor,
			json => File.WriteAllTextAsync(output, json, CancellationToken.None),
			ct => Task.Delay(5000, ct), TestContext.Current.CancellationToken);
		Assert.True(passed, "Live advisor qualification failed; see sanitized report. No automatic retries.");
	}

	private sealed class CaseResult(string id)
	{
		public string Id { get; } = id;
		public string Status { get; set; } = "unrun";
		public long? LatencyMs { get; set; }
		public RouteSearchAdvice? Advice { get; set; }
		public RouteSearchAdvisorFailure? Failure { get; set; }
	}

	private static async Task<bool> RunCasesAsync(AdvisorCase[] cases, IRouteSearchAdvisor advisor,
		Func<string, Task> persist, Func<CancellationToken, Task> pace, CancellationToken cancellationToken)
	{
		var results = cases.Select(c => new CaseResult(c.Id)).ToArray();
		var status = "running";
		Task Save() => persist(JsonSerializer.Serialize(new { contractVersion = "route-search-v1", status, results }, Json));
		try
		{
			await Save();
			for (var i = 0; i < cases.Length; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (i > 0) await pace(cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				var entry = results[i];
				var timer = System.Diagnostics.Stopwatch.StartNew();
				var stop = false;
				try
				{
					var advice = await advisor.AdviseAsync(cases[i].Context, cancellationToken);
					cancellationToken.ThrowIfCancellationRequested();
					entry.Advice = advice;
					entry.Status = RouteSearchProposalPolicy.IsValid(advice, cases[i].Context) ? "passed" : "invalid";
				}
				catch (OperationCanceledException) { entry.Status = "interrupted"; throw; }
				catch (RouteSearchAdvisorException error)
				{
					entry.Status = "error";
					entry.Failure = error.Failure;
					stop = error.Failure is RouteSearchAdvisorFailure.Quota or RouteSearchAdvisorFailure.Authentication or RouteSearchAdvisorFailure.NotConfigured;
				}
				catch { entry.Status = "error"; throw; }
				finally { entry.LatencyMs = timer.ElapsedMilliseconds; }
				await Save();
				if (stop) break;
			}
			var passed = results.All(x => x.Status == "passed");
			status = passed ? "passed" : "failed";
			return passed;
		}
		catch (OperationCanceledException) { status = "interrupted"; throw; }
		catch { status = "failed"; throw; }
		finally { await Save(); }
	}
}
