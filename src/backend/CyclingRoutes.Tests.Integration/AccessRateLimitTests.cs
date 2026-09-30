using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using CyclingRoutes.Api.Access;
using CyclingRoutes.Application.Interpretation;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static CyclingRoutes.Tests.Integration.AccessProtectionTests;

namespace CyclingRoutes.Tests.Integration;

public class AccessRateLimitTests
{
	[Fact]
	public void DailyBudget_AllowsExactly100AndReturns24HourRetryMetadata()
	{
		using var limiter = new FixedWindowRateLimiter(AccessRateLimits.ApiDailyWindow());
		for (var i = 0; i < 100; i++)
		{
			using var lease = limiter.AttemptAcquire();
			Assert.True(lease.IsAcquired);
		}
		using var rejected = limiter.AttemptAcquire();
		Assert.False(rejected.IsAcquired);
		Assert.True(rejected.TryGetMetadata(MetadataName.RetryAfter, out var retry));
		Assert.Equal(TimeSpan.FromHours(24), retry);
	}

	[Fact]
	public async Task ApiMinuteBudget_IsSharedAcrossClientsPathsAndForwardedAddresses()
	{
		using var factory = Factory();
		using var first = Client(factory);
		using var second = Client(factory);
		for (var i = 0; i < 6; i++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/unknown-{i}");
			request.Headers.TryAddWithoutValidation("X-Forwarded-For", $"192.0.2.{i}");
			using var response = await (i % 2 == 0 ? first : second).SendAsync(request, TestContext.Current.CancellationToken);
			Assert.Equal(i < 5 ? HttpStatusCode.NotFound : HttpStatusCode.TooManyRequests, response.StatusCode);
			if (i == 5) await AssertRateLimited(response, 60);
		}
		using var health = await first.GetAsync("/health", TestContext.Current.CancellationToken);
		Assert.Equal(HttpStatusCode.OK, health.StatusCode);
	}

	[Fact]
	public async Task LoginBudget_IsGlobalAndDoesNotPermitFurtherGuessesAfterExhaustion()
	{
		using var factory = Factory();
		using var client = Client(factory, false);
		for (var i = 0; i < 21; i++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, i % 2 == 0 ? "/" : "/api/unknown");
			request.Headers.TryAddWithoutValidation("X-Forwarded-For", $"192.0.2.{i}");
			if (i % 2 == 0) request.Headers.Authorization = Credentials(password: "wrong");
			using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
			Assert.Equal(i < 20 ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests, response.StatusCode);
			if (i == 20) await AssertRateLimited(response, 60);
		}
		client.DefaultRequestHeaders.Authorization = Credentials();
		using var blocked = await client.GetAsync("/api/unknown", TestContext.Current.CancellationToken);
		Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
	}

	[Fact]
	public async Task TwoConcurrentApiRequests_RejectThirdWithoutQueuingAndReleaseOnCompletion()
	{
		var interpreter = new BlockingInterpreter();
		using var factory = Factory().WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IRouteIntentInterpreter>(interpreter)));
		using var client = Client(factory);
		using var request1 = Post();
		using var request2 = Post();
		var first = client.SendAsync(request1, TestContext.Current.CancellationToken);
		var second = client.SendAsync(request2, TestContext.Current.CancellationToken);
		try
		{
			await interpreter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
			using var request3 = Post();
			using var rejected = await client.SendAsync(request3, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
			Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
			await AssertRateLimited(rejected, 1);
			Assert.Equal(2, interpreter.Calls);
		}
		finally
		{
			interpreter.Release.TrySetResult();
			using var response1 = await first;
			using var response2 = await second;
		}
		using var request4 = Post();
		using var allowed = await client.SendAsync(request4, TestContext.Current.CancellationToken);
		Assert.Equal(HttpStatusCode.ServiceUnavailable, allowed.StatusCode);
		Assert.Equal(3, interpreter.Calls);
	}

	private static async Task AssertRateLimited(HttpResponseMessage response, int maximumSeconds)
	{
		Assert.NotNull(response.Headers.RetryAfter?.Delta);
		Assert.InRange(response.Headers.RetryAfter.Delta.Value.TotalSeconds, 1, maximumSeconds);
		Assert.Equal("access_rate_limited", (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString());
	}

	private sealed class BlockingInterpreter : IRouteIntentInterpreter
	{
		public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public int Calls;
		public async Task<RouteIntentExtraction> InterpretAsync(string prompt, string locale, CancellationToken cancellationToken)
		{
			if (Interlocked.Increment(ref Calls) == 2) Entered.TrySetResult();
			await Release.Task.WaitAsync(cancellationToken);
			throw new InterpretationException(InterpretationFailure.NotConfigured);
		}
	}
}
