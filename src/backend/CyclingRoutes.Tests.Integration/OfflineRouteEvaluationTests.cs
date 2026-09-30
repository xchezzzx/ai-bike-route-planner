using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Evaluation;

namespace CyclingRoutes.Tests.Integration;

public class OfflineRouteEvaluationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Triangle = """
        <gpx xmlns="http://www.topografix.com/GPX/1/1" version="1.1">
          <metadata><name>private track</name></metadata>
          <trk><trkseg><trkpt lat="0" lon="0"/><trkpt lat="0" lon="0.01"/>
          <trkpt lat="0.01" lon="0.01"/><trkpt lat="0" lon="0"/></trkseg></trk>
        </gpx>
        """;

    [Fact]
    public async Task GpxUsesProductionGeometryAndLeavesSurfaceAndDurationUnknown()
    {
        var report = await OfflineRouteEvaluator.GpxAsync(Bytes(Triangle), Ct);
        Assert.Equal(4, report.PointCount);
        Assert.InRange(report.GeometryLengthMeters, 3790, 3800);
        Assert.Equal(0, report.ClosureGapMeters);
        Assert.Equal(report.GeometryLengthMeters, report.Quality.Surface.UnknownMeters);
        Assert.Equal("Unavailable", report.Quality.SurfaceEvidenceState.ToString());
        Assert.Null(report.ProviderDistanceMeters);
        Assert.Null(report.ProviderDurationSeconds);
        Assert.Null(report.PolicyEligible);
        Assert.Equal(0, report.Quality.RemainingRepeatedMeters);
        var json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("private track", json);
        Assert.DoesNotContain("latitude", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("longitude", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(64, report.InputSha256.Length);
    }

    [Fact]
    public async Task GpxDetectsExactInternalRetracingAndAnOpenTrack()
    {
        const string gpx = """
            <gpx xmlns="http://www.topografix.com/GPX/1/0"><trk><trkseg>
            <trkpt lat="0" lon="0"/><trkpt lat="0" lon="0.01"/>
            <trkpt lat="0.01" lon="0.01"/><trkpt lat="0" lon="0.01"/>
            <trkpt lat="0" lon="0.02"/></trkseg></trk></gpx>
            """;
        var report = await OfflineRouteEvaluator.GpxAsync(Bytes(gpx), Ct);
        Assert.InRange(report.ClosureGapMeters, 2223, 2225);
        Assert.InRange(report.Quality.RemainingRepeatedMeters, 1111, 1113);
        Assert.Equal(0, report.Quality.SharedStemMeters);
    }

    [Theory]
    [InlineData("<gpx><trk><trkseg><trkpt lat='0' lon='0'/><trkpt lat='1' lon='1'/></trkseg></trk></gpx>", typeof(InvalidDataException))]
    [InlineData("<gpx xmlns='http://www.topografix.com/GPX/1/1'><trk><trkseg/><trkseg/></trk></gpx>", typeof(InvalidDataException))]
    [InlineData("<gpx xmlns='http://www.topografix.com/GPX/1/1'><trk><trkseg><trkpt lat='NaN' lon='0'/></trkseg></trk></gpx>", typeof(InvalidDataException))]
    [InlineData("<gpx xmlns='http://www.topografix.com/GPX/1/1'><trk><trkseg><trkpt lat='0' lon='0'/><trkpt lat='0' lon='0'/></trkseg></trk></gpx>", typeof(RoutingException))]
    [InlineData("<!DOCTYPE gpx [<!ENTITY x SYSTEM 'file:///never-read'>]><gpx xmlns='http://www.topografix.com/GPX/1/1'>&x;</gpx>", typeof(XmlException))]
    public async Task InvalidOrDiscontinuousGpxFailsWithoutGuessing(string input, Type expected)
    {
        await Assert.ThrowsAsync(expected, () => OfflineRouteEvaluator.GpxAsync(Bytes(input), Ct));
    }

    [Fact]
    public async Task OrsReplayUsesProductionSurfaceExclusionDespiteMatchingDistance()
    {
        var report = await OfflineRouteEvaluator.OrsAsync(Bytes(Ors(40000, 10)), 35000, 45000, Ct);
        Assert.Equal(40000, report.ProviderDistanceMeters);
        Assert.Equal(7200, report.ProviderDurationSeconds);
        Assert.True(report.TargetsMatched);
        Assert.False(report.PolicyEligible);
        Assert.Equal(new[] { "road_surface_limit_exceeded" }, report.Reasons);
        Assert.Equal(report.GeometryLengthMeters, report.Quality.Surface.NonRoadMeters);
    }

    [Fact]
    public async Task OrsReplayUsesExactRangeAndUnknownIsNotPaved()
    {
        var report = await OfflineRouteEvaluator.OrsAsync(Bytes(Ors(45001, null)), 35000, 45000, Ct);
        Assert.False(report.TargetsMatched);
        Assert.Equal(new[] { "targets_not_met" }, report.Reasons);
        Assert.Equal(0, report.Quality.Surface.PavedMeters);
        Assert.Equal(report.GeometryLengthMeters, report.Quality.Surface.UnknownMeters);
        var boundary = await OfflineRouteEvaluator.OrsAsync(Bytes(Ors(45000, 3)), 35000, 45000, Ct);
        Assert.True(boundary.PolicyEligible);
        var unknown = await OfflineRouteEvaluator.OrsAsync(Bytes(Ors(40000, null)), 35000, 45000, Ct);
        Assert.True(unknown.PolicyEligible);
        Assert.Contains("road_surface_unknown", unknown.Warnings);
    }

    [Theory]
    [InlineData("km")]
    [InlineData("mi")]
    [InlineData("unknown")]
    public async Task SavedNonMeterUnitsAreRejectedInsteadOfMisreportingEligibility(string units)
    {
        var json = JsonNode.Parse(Ors(40, null))!;
        json["metadata"] = JsonSerializer.SerializeToNode(new { query = new { units } });
        await Assert.ThrowsAsync<InvalidDataException>(() => OfflineRouteEvaluator.OrsAsync(Bytes(json.ToJsonString()), 35000, 45000, Ct));
        json["metadata"] = JsonSerializer.SerializeToNode(new { query = new { units = "m" } });
        json["features"]![0]!["properties"]!["summary"]!["distance"] = 40000;
        Assert.True((await OfflineRouteEvaluator.OrsAsync(Bytes(json.ToJsonString()), 35000, 45000, Ct)).PolicyEligible);
    }

    [Fact]
    public async Task RetainedRetracingWarningsMatchProductionPolicy()
    {
        var json = JsonNode.Parse(Ors(40000, null))!;
        json["features"]![0]!["geometry"]!["coordinates"] = JsonSerializer.SerializeToNode(new double[][]
            { [0, 0], [0.01, 0], [0.01, 0.01], [0.01, 0], [0.02, 0], [0, 0] });
        var report = await OfflineRouteEvaluator.OrsAsync(Bytes(json.ToJsonString()), 35000, 45000, Ct);
        Assert.True(report.PolicyEligible);
        Assert.Contains("road_retracing", report.Warnings);
        Assert.Contains("road_surface_unknown", report.Warnings);
    }

    [Fact]
    public async Task FailedSerializationLeavesNoFinalReportAndCanBeRetried()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var output = Path.Combine(folder, "report.json");
        var report = await OfflineRouteEvaluator.GpxAsync(Bytes(Triangle), Ct);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => OfflineReportWriter.WriteAsync(output, report with { Reasons = new FailingReasons() }, Ct));
            Assert.False(File.Exists(output));
            Assert.Empty(Directory.GetFiles(folder));
            await OfflineReportWriter.WriteAsync(output, report, Ct);
            Assert.True(File.Exists(output));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task MalformedOrsAndCancellationCannotProduceAReport()
    {
        await Assert.ThrowsAsync<RoutingException>(() => OfflineRouteEvaluator.OrsAsync(Bytes("{}"), 35000, 45000, Ct));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OfflineRouteEvaluator.GpxAsync(Bytes(Triangle), cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OfflineRouteEvaluator.OrsAsync(Bytes(Ors(40000, 3)), 35000, 45000, cancelled.Token));
    }

    [Fact]
    public async Task InputSizeAndReversedRangeAreRejected()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => OfflineRouteEvaluator.GpxAsync([], Ct));
        await Assert.ThrowsAsync<InvalidDataException>(() => OfflineRouteEvaluator.GpxAsync(new byte[OfflineRouteEvaluator.MaxInputBytes + 1], Ct));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => OfflineRouteEvaluator.OrsAsync(Bytes(Ors(40000, 3)), 45000, 35000, Ct));
    }

    [Fact]
    public async Task CliWritesRedactedReportAndNeverOverwritesInputOrEarlierEvidence()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var input = Path.Combine(folder, "private-name.gpx");
            var output = Path.Combine(folder, "report.json");
            await File.WriteAllTextAsync(input, Triangle, Ct);
            Assert.Equal(0, await EvaluationCli.Main(["gpx", input, output]));
            var original = await File.ReadAllTextAsync(output, Ct);
            using var json = JsonDocument.Parse(original);
            Assert.Equal("unavailable", json.RootElement.GetProperty("quality").GetProperty("surfaceEvidenceState").GetString());
            Assert.DoesNotContain(folder, original);
            Assert.DoesNotContain("private-name", original);
            Assert.Equal(1, await EvaluationCli.Main(["gpx", input, output]));
            Assert.Equal(original, await File.ReadAllTextAsync(output, Ct));
            Assert.Equal(1, await EvaluationCli.Main(["gpx", input, input]));
            Assert.Equal(Triangle, await File.ReadAllTextAsync(input, Ct));
            var malformed = Path.Combine(folder, "bad.gpx");
            await File.WriteAllTextAsync(malformed, "<", Ct);
            var absent = Path.Combine(folder, "must-not-exist.json");
            Assert.Equal(1, await EvaluationCli.Main(["gpx", malformed, absent]));
            Assert.False(File.Exists(absent));
            Assert.Equal(2, await EvaluationCli.Main([]));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private sealed class FailingReasons : IReadOnlyList<string>
    {
        public int Count => 1;
        public string this[int index] => throw new IOException("Synthetic write failure");
        public IEnumerator<string> GetEnumerator() => throw new IOException("Synthetic write failure");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private static string Ors(double distance, int? surface) => JsonSerializer.Serialize(new
    {
        type = "FeatureCollection",
        features = new[] { new
        {
            geometry = new { type = "LineString", coordinates = new[] { new[] { 0d, 0d },
                [0.01, 0d], [0.01, 0.01], [0d, 0d] } },
            properties = new
            {
                summary = new { distance, duration = 7200 },
                extras = surface is { } code ? new { surface = new { values = new[] { new[] { 0, 3, code } } } } : null
            }
        } }
    });
}
