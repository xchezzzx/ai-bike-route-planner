using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Infrastructure.Routing;

public sealed class OpenRouteServiceProvider(HttpClient client, OpenRouteServiceOptions options) : IRoutingProvider
{
	private const string Endpoint = "https://api.heigit.org/openrouteservice/v2/directions/cycling-road/geojson";
	private const string Attribution = "openrouteservice.org | OpenStreetMap contributors";

	public Task<RoutedPath> GetRoadRouteAsync(GeoCoordinate start, GeoCoordinate destination, CancellationToken cancellationToken) =>
		SendAsync(new
		{
			coordinates = new[] { new[] { start.Longitude, start.Latitude }, new[] { destination.Longitude, destination.Latitude } },
			elevation = true,
			instructions = false,
			units = "m",
			options = new { avoid_features = new[] { "ferries", "fords", "steps" } }
		}, cancellationToken);

	public Task<RoutedPath> GetRoadLoopAsync(GeoCoordinate start, double requestedLengthMeters, int seed, CancellationToken cancellationToken) =>
		SendAsync(new
		{
			coordinates = new[] { new[] { start.Longitude, start.Latitude } },
			elevation = true,
			instructions = false,
			units = "m",
			options = new
			{
				avoid_features = new[] { "ferries", "fords", "steps" },
				round_trip = new { length = requestedLengthMeters, points = 3, seed }
			}
		}, cancellationToken);

	private async Task<RoutedPath> SendAsync<T>(T payload, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (string.IsNullOrWhiteSpace(options.ApiKey)) throw new RoutingException(RoutingFailure.NotConfigured);
		using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
		if (!request.Headers.TryAddWithoutValidation("Authorization", options.ApiKey))
			throw new RoutingException(RoutingFailure.CredentialsRejected);
		request.Content = JsonContent.Create(payload);

		try
		{
			using var response = await client.SendAsync(request, cancellationToken);
			if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
				throw new RoutingException(RoutingFailure.CredentialsRejected);
			if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new RoutingException(RoutingFailure.RateLimited);
			if ((int)response.StatusCode >= 500) throw new RoutingException(RoutingFailure.Unavailable);
			string body;
			try { body = await response.Content.ReadAsStringAsync(cancellationToken); }
			catch (InvalidOperationException) { throw InvalidResponse(); }
			if (!response.IsSuccessStatusCode)
			{
				throw new RoutingException(ParseFailure(body));
			}
			return ParseRoute(body);
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

	private static RoutingFailure ParseFailure(string body)
	{
		try
		{
			using var json = JsonDocument.Parse(body);
			if (json.RootElement.ValueKind == JsonValueKind.Object
				&& json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
				&& error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number
				&& code.TryGetInt32(out var value))
				return value switch
				{
					2004 => RoutingFailure.LimitExceeded,
					2009 or 2010 => RoutingFailure.NoRoute,
					_ => RoutingFailure.InvalidResponse
				};
		}
		catch (JsonException) { }
		return RoutingFailure.InvalidResponse;
	}

	private static RoutedPath ParseRoute(string body)
	{
		try
		{
			using var json = JsonDocument.Parse(body);
			var root = json.RootElement;
			if (root.GetProperty("type").GetString() != "FeatureCollection") throw InvalidResponse();
			var features = root.GetProperty("features");
			if (features.GetArrayLength() == 0) throw InvalidResponse();
			var feature = features[0];
			var geometry = feature.GetProperty("geometry");
			if (geometry.GetProperty("type").GetString() != "LineString") throw InvalidResponse();
			var points = new List<RoutePoint>();
			foreach (var coordinate in geometry.GetProperty("coordinates").EnumerateArray())
			{
				if (coordinate.GetArrayLength() is not (2 or 3)) throw InvalidResponse();
				var position = new GeoCoordinate(Number(coordinate[1]), Number(coordinate[0]));
				double? elevation = coordinate.GetArrayLength() == 3 ? Number(coordinate[2]) : null;
				points.Add(new(position, elevation));
			}
			if (points.Count < 2) throw InvalidResponse();
			var properties = feature.GetProperty("properties");
			var summary = properties.GetProperty("summary");
			var distance = Number(summary.GetProperty("distance"));
			var duration = Number(summary.GetProperty("duration"));
			if (distance <= 0 || duration <= 0) throw InvalidResponse();
			return new(points.AsReadOnly(), distance, duration,
				OptionalMetric(properties, "ascent"), OptionalMetric(properties, "descent"), Attribution);
		}
		catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
		{
			throw InvalidResponse();
		}
	}

	private static double Number(JsonElement value)
	{
		if (!value.TryGetDouble(out var number) || !double.IsFinite(number)) throw InvalidResponse();
		return number;
	}

	private static double? OptionalMetric(JsonElement properties, string name)
	{
		if (!properties.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
		var number = Number(value);
		if (number < 0) throw InvalidResponse();
		return number;
	}

	private static RoutingException InvalidResponse() => new(RoutingFailure.InvalidResponse);
}
