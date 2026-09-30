using System.Globalization;
using System.Threading.RateLimiting;

namespace CyclingRoutes.Api.Access;

public sealed class AccessRateLimits : IDisposable
{
	public FixedWindowRateLimiter LoginAttempts { get; } = new(Window(20, TimeSpan.FromMinutes(1)));

	public static FixedWindowRateLimiterOptions ApiDailyWindow() => Window(100, TimeSpan.FromHours(24));

	public static PartitionedRateLimiter<HttpContext> CreateApiLimiter() =>
		PartitionedRateLimiter.CreateChained(
			Partition(context => RateLimitPartition.GetConcurrencyLimiter("api", _ => new()
			{ PermitLimit = 2, QueueLimit = 0, QueueProcessingOrder = QueueProcessingOrder.OldestFirst })),
			Partition(context => RateLimitPartition.GetFixedWindowLimiter("api", _ => Window(5, TimeSpan.FromMinutes(1)))),
			Partition(context => RateLimitPartition.GetFixedWindowLimiter("api", _ => ApiDailyWindow())));

	// All clients and API paths share a single partition; client IP is not a trust boundary.
	private static PartitionedRateLimiter<HttpContext> Partition(Func<HttpContext, RateLimitPartition<string>> apiPartition) =>
		PartitionedRateLimiter.Create<HttpContext, string>(context =>
			context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<AccessOptions>>().Value.IsLocal ||
			!context.Request.Path.StartsWithSegments("/api")
				? RateLimitPartition.GetNoLimiter("unlimited") : apiPartition(context));

	private static FixedWindowRateLimiterOptions Window(int permits, TimeSpan window) => new()
	{
		PermitLimit = permits, Window = window, QueueLimit = 0,
		QueueProcessingOrder = QueueProcessingOrder.OldestFirst, AutoReplenishment = true
	};

	public static Task RejectAsync(HttpContext context, RateLimitLease lease)
	{
		context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
		var seconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ? Math.Max(1, Math.Ceiling(retry.TotalSeconds)) : 1;
		context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
		return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests,
			extensions: new Dictionary<string, object?> { ["code"] = "access_rate_limited" }).ExecuteAsync(context);
	}

	public void Dispose() => LoginAttempts.Dispose();
}
