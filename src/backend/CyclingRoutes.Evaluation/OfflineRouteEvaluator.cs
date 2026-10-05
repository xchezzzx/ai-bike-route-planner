using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;
using CyclingRoutes.Infrastructure.Routing;

namespace CyclingRoutes.Evaluation;

public sealed record OfflineRouteReport(
    string InputSha256, string Source, int PointCount, double GeometryLengthMeters,
    double ClosureGapMeters, double? ProviderDistanceMeters, double? ProviderDurationSeconds,
    double? TargetMinimumMeters, double? TargetMaximumMeters, bool? TargetsMatched,
    bool? PolicyEligible, IReadOnlyList<string> Reasons, IReadOnlyList<string> Warnings, RoadQualityAssessment Quality)
{
    public double? NearReturnMeters { get; init; }
}

public static class OfflineRouteEvaluator
{
    public const int MaxInputBytes = 16 * 1024 * 1024;

    public static async Task<OfflineRouteReport> GpxAsync(byte[] input, CancellationToken ct)
    {
        Check(input, ct);
        using var stream = new MemoryStream(input, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaxInputBytes
        });
        var document = await XDocument.LoadAsync(reader, LoadOptions.None, ct);
        var root = document.Root ?? throw new InvalidDataException();
        var ns = root.Name.Namespace;
        if (root.Name.LocalName != "gpx" || ns.NamespaceName is not
            ("http://www.topografix.com/GPX/1/1" or "http://www.topografix.com/GPX/1/0"))
            throw new InvalidDataException();
        var tracks = root.Elements(ns + "trk").ToArray();
        if (tracks.Length != 1 || root.Elements(ns + "rte").Any()) throw new InvalidDataException();
        var segments = tracks[0].Elements(ns + "trkseg").ToArray();
        if (segments.Length != 1) throw new InvalidDataException();
        var points = new List<RoutePoint>();
        foreach (var point in segments[0].Elements(ns + "trkpt"))
        {
            ct.ThrowIfCancellationRequested();
            if (points.Count >= 200000) throw new InvalidDataException();
            var elevation = point.Element(ns + "ele");
            points.Add(new(new(Number(point.Attribute("lat")?.Value), Number(point.Attribute("lon")?.Value)),
                elevation is null ? null : Number(elevation.Value)));
        }
        if (points.Count < 2) throw new InvalidDataException();
        // GPX has no trustworthy provider summary, riding duration or road evidence.
        var path = new RoutedPath(points.AsReadOnly(), 1, 1, null, null, "offline-gpx");
        return Report(input, "gpx", path, null, null, null, null, null, null, [], [], ct);
    }

    public static async Task<OfflineRouteReport> OrsAsync(byte[] input, double minMeters, double maxMeters, CancellationToken ct)
    {
        Check(input, ct);
        ValidateUnits(input);
        _ = new DistanceRange(minMeters, maxMeters);
        using var client = new HttpClient(new SavedResponseHandler(input));
        var provider = new OpenRouteServiceProvider(client, new() { ApiKey = "offline-placeholder" });
        var path = await provider.GetRoadLoopAsync(new(0, 0), (minMeters + maxMeters) / 2, 1, ct);
        return SelectReport(input, "ors", path, minMeters, maxMeters, ct);
    }

    public static async Task<OfflineRouteReport> GraphHopperAsync(byte[] input, double minMeters, double maxMeters, CancellationToken ct)
    {
        Check(input, ct);
        _ = new DistanceRange(minMeters, maxMeters);
        using var client = new HttpClient(new SavedResponseHandler(input));
        var provider = new GraphHopperProvider(client, new());
        var path = await provider.GetRoadLoopAsync(new(0, 0), (minMeters + maxMeters) / 2, 1, ct);
        return SelectReport(input, "graphhopper", path, minMeters, maxMeters, ct);
    }

    private static OfflineRouteReport SelectReport(byte[] input, string source, RoutedPath path,
        double minMeters, double maxMeters, CancellationToken ct)
    {
        // Mirror the loop service's structural gate before assessing eligibility.
        if (path.Points.Count < 4 || path.Points[0].Position != path.Points[^1].Position
            || path.Points.Select(x => x.Position).Distinct().Take(3).Count() < 3)
            throw new InvalidDataException();
        var intent = new RouteIntent(path.Points[0].Position, RouteShape.Loop, CyclingProfile.Road,
            targetDistanceRange: new(minMeters, maxMeters));
        var selected = new RoadCandidateSelector(new(), new()).Select(intent, [new(1, path)], ct);
        var retained = selected.Retained.Count == 1;
        var assessment = retained ? selected.Retained[0].Assessment : selected.Excluded[0].Assessment;
        return Report(input, source, path, path.DistanceMeters, path.EstimatedDurationSeconds,
            minMeters, maxMeters, assessment.TargetsMatched, retained,
            retained ? [] : selected.Excluded[0].Reasons, retained ? selected.Retained[0].Warnings : [], ct);
    }

    private static OfflineRouteReport Report(byte[] input, string source, RoutedPath path,
        double? providerDistance, double? providerDuration, double? min, double? max,
        bool? matched, bool? eligible, IReadOnlyList<string> reasons, IReadOnlyList<string> warnings, CancellationToken ct)
    {
        var quality = new RoadQualityAssessor().Assess(path, ct);
        return new(Convert.ToHexStringLower(SHA256.HashData(input)), source, path.Points.Count,
            quality.GeometryLengthMeters, RouteGeometryMetrics.DistanceMeters(path.Points[0].Position, path.Points[^1].Position),
            providerDistance, providerDuration, min, max, matched, eligible, reasons, warnings, quality)
        { NearReturnMeters = LoopGeometryMetrics.NearReturnMeters(path, ct) };
    }

    private static void ValidateUnits(byte[] input)
    {
        using var json = JsonDocument.Parse(input);
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        if (json.RootElement.TryGetProperty("metadata", out var metadata)
            && metadata.ValueKind == JsonValueKind.Object
            && metadata.TryGetProperty("query", out var query) && query.ValueKind == JsonValueKind.Object
            && query.TryGetProperty("units", out var units)
            && (units.ValueKind != JsonValueKind.String || units.GetString() != "m"))
            throw new InvalidDataException();
    }

    private static double Number(string? text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new InvalidDataException();
        return value;
    }

    private static void Check(byte[] input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (input.Length is 0 or > MaxInputBytes) throw new InvalidDataException();
    }

    private sealed class SavedResponseHandler(byte[] input) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Encoding.UTF8.GetString(input), Encoding.UTF8, "application/json")
            });
        }
    }
}
