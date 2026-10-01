using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Infrastructure.Routing;

public sealed class GraphHopperProvider(HttpClient client, GraphHopperOptions options) : IRoutingProvider
{
	public Task<RoutedPath> GetRoadRouteAsync(GeoCoordinate start, GeoCoordinate destination, CancellationToken cancellationToken) =>
		SendAsync(Payload([start, destination]), cancellationToken);

	public Task<RoutedPath> GetRoadLoopAsync(GeoCoordinate start, double requestedLengthMeters, int seed, CancellationToken cancellationToken)
	{
		if (!double.IsFinite(requestedLengthMeters) || requestedLengthMeters <= 0)
			throw new RoutingException(RoutingFailure.LimitExceeded);
		var payload = Payload([start]);
		payload["algorithm"] = "round_trip";
		payload["round_trip.distance"] = requestedLengthMeters;
		payload["round_trip.seed"] = seed;
		return SendAsync(payload, cancellationToken);
	}

	private Dictionary<string, object> Payload(GeoCoordinate[] points) => new()
	{
		["points"] = points.Select(p => new[] { p.Longitude, p.Latitude }).ToArray(),
		["profile"] = options.Profile,
		["points_encoded"] = false,
		["elevation"] = true,
		["instructions"] = true,
		["details"] = new[] { "surface", "road_class", "road_environment" },
		["timeout_ms"] = 14000
	};

	private async Task<RoutedPath> SendAsync(Dictionary<string, object> payload, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!options.IsValid()) throw new RoutingException(RoutingFailure.NotConfigured);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(TimeSpan.FromSeconds(15));
		using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.BaseUrl), "route"))
		{
			Content = JsonContent.Create(payload)
		};
		try
		{
			using var response = await client.SendAsync(request, deadline.Token);
			if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
				throw new RoutingException(RoutingFailure.CredentialsRejected);
			if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new RoutingException(RoutingFailure.RateLimited);
			if ((int)response.StatusCode >= 500) throw new RoutingException(RoutingFailure.Unavailable);
			string body;
			try { body = await response.Content.ReadAsStringAsync(deadline.Token); }
			catch (InvalidOperationException) { throw new RoutingException(RoutingFailure.InvalidResponse); }
			if (!response.IsSuccessStatusCode) throw new RoutingException(ParseFailure(body, payload.ContainsKey("round_trip.distance")));
			return GraphHopperResponseParser.Parse(body, deadline.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new RoutingException(RoutingFailure.Timeout);
		}
		catch (HttpRequestException)
		{
			throw new RoutingException(RoutingFailure.Unavailable);
		}
	}

	private static RoutingFailure ParseFailure(string body, bool roundTrip)
	{
		try
		{
			using var json = JsonDocument.Parse(body);
			if (json.RootElement.ValueKind != JsonValueKind.Object
				|| !json.RootElement.TryGetProperty("hints", out var hints) || hints.ValueKind != JsonValueKind.Array)
				return RoutingFailure.InvalidResponse;
			foreach (var hint in hints.EnumerateArray())
			{
				if (hint.ValueKind != JsonValueKind.Object || !hint.TryGetProperty("details", out var details)
					|| details.ValueKind != JsonValueKind.String) continue;
				// The pinned 11.1 native loop lookup uses a generic exception for this
				// ordinary no-route outcome; do not classify other argument errors as no-route.
				if (roundTrip && details.GetString() == "java.lang.IllegalArgumentException"
					&& hint.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
					&& message.GetString()!.StartsWith("Could not find a valid point after 3 tries, for the point:", StringComparison.Ordinal))
					return RoutingFailure.NoRoute;
				var failure = details.GetString() switch
				{
					"com.graphhopper.util.exceptions.ConnectionNotFoundException"
						or "com.graphhopper.util.exceptions.PointNotFoundException"
						or "com.graphhopper.util.exceptions.PointOutOfBoundsException" => RoutingFailure.NoRoute,
					"com.graphhopper.util.exceptions.MaximumNodesExceededException" => RoutingFailure.LimitExceeded,
					_ => RoutingFailure.InvalidResponse
				};
				if (failure != RoutingFailure.InvalidResponse) return failure;
			}
		}
		catch (JsonException) { }
		return RoutingFailure.InvalidResponse;
	}
}
