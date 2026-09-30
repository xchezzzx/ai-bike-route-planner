using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace CyclingRoutes.Api.Access;

public sealed class AccessMiddleware
{
	private readonly RequestDelegate next;
	private readonly AccessOptions options;
	private readonly byte[] credentialHash;

	public AccessMiddleware(RequestDelegate next, IOptions<AccessOptions> options)
	{
		this.next = next;
		this.options = options.Value;
		credentialHash = SHA256.HashData(Encoding.UTF8.GetBytes("tester:" + this.options.Password));
	}

	public async Task InvokeAsync(HttpContext context, AccessRateLimits limits)
	{
		context.Response.OnStarting(() =>
		{
			var headers = context.Response.Headers;
			headers.CacheControl = "no-store";
			headers.XContentTypeOptions = "nosniff";
			headers.XFrameOptions = "DENY";
			headers["Referrer-Policy"] = "no-referrer";
			headers["Permissions-Policy"] = "geolocation=(self), camera=(), microphone=()";
			if (!options.IsLocal) headers.StrictTransportSecurity = "max-age=31536000";
			return Task.CompletedTask;
		});

		var request = context.Request;
		if (options.IsLocal || request.Path == "/health" && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)))
		{
			await next(context);
			return;
		}

		// Bound verification itself, including successful guesses, before inspecting credentials.
		using (var lease = limits.LoginAttempts.AttemptAcquire())
		{
			if (!lease.IsAcquired)
			{
				await AccessRateLimits.RejectAsync(context, lease);
				return;
			}
		}
		if (!HasValidCredentials(request))
		{
			context.Response.Headers.WWWAuthenticate = "Basic realm=\"Closed testers\", charset=\"UTF-8\"";
			await Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
				extensions: new Dictionary<string, object?> { ["code"] = "access_unauthorized" }).ExecuteAsync(context);
			return;
		}

		if (request.Path.StartsWithSegments("/api") &&
			!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method) &&
			(request.Headers.Origin.Count != 1 || !string.Equals(request.Headers.Origin[0], options.PublicOrigin, StringComparison.Ordinal) ||
			 request.Headers["Sec-Fetch-Site"].Any(value => value?.Split(',').Any(site => site.Trim().Equals("cross-site", StringComparison.OrdinalIgnoreCase)) == true)))
		{
			await Results.Problem(statusCode: StatusCodes.Status403Forbidden,
				extensions: new Dictionary<string, object?> { ["code"] = "access_origin_forbidden" }).ExecuteAsync(context);
			return;
		}

		await next(context);
	}

	private bool HasValidCredentials(HttpRequest request)
	{
		var values = request.Headers.Authorization;
		if (values.Count != 1 || values[0] is not { Length: <= 1024 } value ||
			!value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return false;

		Span<byte> decoded = stackalloc byte[768];
		if (!Convert.TryFromBase64String(value[6..], decoded, out var count)) return false;
		Span<byte> hash = stackalloc byte[32];
		SHA256.HashData(decoded[..count], hash);
		var valid = CryptographicOperations.FixedTimeEquals(hash, credentialHash);
		CryptographicOperations.ZeroMemory(decoded);
		return valid;
	}
}
