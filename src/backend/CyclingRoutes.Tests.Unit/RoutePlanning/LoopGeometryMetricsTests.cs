using CyclingRoutes.Application.Routing;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class LoopGeometryMetricsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void SustainedParallelReturn_IsDetectedWithoutIdenticalEdges()
    {
        var result = LoopGeometryMetrics.NearReturnMeters(ParallelReturn(), Ct);
        // Of the 2 km return, about 250 m around the reversal is below the progress-separation guard.
        Assert.InRange(result!.Value, 1650, 1900);
        Assert.Equal(0, new RoadQualityAssessor().Assess(ParallelReturn(), Ct).RemainingRepeatedMeters);
    }

    [Fact]
    public void OrdinaryRing_HasNoNearReturn()
    {
        Assert.Equal(0, LoopGeometryMetrics.NearReturnMeters(Meters((0, 0), (2000, 0), (2000, 2000), (0, 2000), (0, 0)), Ct));
    }

    [Fact]
    public void SharedDepartureStem_IsNotAnInternalReturn()
    {
        Assert.Equal(0, LoopGeometryMetrics.NearReturnMeters(Meters((0, 0), (2000, 0), (4000, 0),
            (4000, 2000), (2000, 2000), (2000, 0), (0, 0)), Ct));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(5)]
    public void ShortSerpentineLegs_DoNotBecomeSustainedReturns(double spacing)
    {
        var vertices = new List<(double, double)> { (0, 0), (1000, 0) };
        for (var i = 0; i < 12; i++)
        {
            vertices.Add((i % 2 == 0 ? 1200 : 1000, 10 + i * spacing));
        }
        vertices.Add((2000, 2000)); vertices.Add((0, 2000)); vertices.Add((0, 0));
        Assert.Equal(0, LoopGeometryMetrics.NearReturnMeters(Meters(vertices.ToArray()), Ct));
    }

    [Fact]
    public void SharedStem_WithSmallDetourIsStillExempt()
    {
        var path = Meters((0, 0), (500, 0), (500, 30), (520, 30), (520, 0), (2000, 0),
            (4000, 0), (4000, 2000), (2000, 2000), (2000, 0), (0, 0));
        Assert.Equal(0, LoopGeometryMetrics.NearReturnMeters(path, Ct));
        Assert.Equal(0, LoopGeometryMetrics.NearReturnMeters(path with { Points = path.Points.Reverse().ToArray() }, Ct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void NearToleranceBoundary_DoesNotDependOnSamplingPhase(double startOffset)
    {
        var path = Meters((startOffset, 0), (1000, 1000), (3000, 1000), (3000, 1024),
            (1000, 1024), (-1000, 2000), (startOffset, 0));
        Assert.InRange(LoopGeometryMetrics.NearReturnMeters(path, Ct)!.Value, 1650, 1900);
    }

    [Fact]
    public void SmallRoundaboutAndCrossing_DoNotCountAsReturns()
    {
        Assert.Equal(0, LoopGeometryMetrics.NearReturnMeters(Meters((0, 0), (1000, 0), (1020, 20),
            (1000, 40), (980, 20), (1000, 0), (2000, 0), (1000, 2000), (1000, -1000), (0, 0)), Ct));
    }

    [Fact]
    public void GraduallyCurvingOppositePassages_RemainOneSustainedRun()
    {
        var vertices = new List<(double, double)> { (0, 0) };
        for (var i = 0; i <= 120; i++)
        {
            var angle = i * 1.7 * Math.PI / 120;
            vertices.Add((2000 + 250 * Math.Cos(angle), 2000 + 250 * Math.Sin(angle)));
        }
        for (var i = 120; i >= 0; i--)
        {
            var angle = i * 1.7 * Math.PI / 120;
            vertices.Add((2000 + 260 * Math.Cos(angle), 2000 + 260 * Math.Sin(angle)));
        }
        vertices.Add((3000, -1000)); vertices.Add((0, 0));
        var path = Meters(vertices.ToArray());
        Assert.InRange(LoopGeometryMetrics.NearReturnMeters(path, Ct)!.Value, 900, 1400);
        Assert.InRange(LoopGeometryMetrics.NearReturnMeters(path with { Points = path.Points.Reverse().ToArray() }, Ct)!.Value, 900, 1400);
    }

    [Fact]
    public void SameDirectionParallelPassages_AreNotCalledReversePassages()
    {
        Assert.Equal(0, LoopGeometryMetrics.NearReturnMeters(Meters((0, 0), (1000, 1000), (3000, 1000),
            (4000, 2000), (1000, 2000), (1000, 1010), (3000, 1010), (4000, -1000), (0, 0)), Ct));
    }

    [Fact]
    public void SamplingAndReversingTrack_DoNotChangeTheSignalMaterially()
    {
        var path = ParallelReturn();
        var dense = path.Points.Zip(path.Points.Skip(1)).SelectMany(pair => Enumerable.Range(0, 10)
            .Select(i => new RoutePoint(new(pair.First.Position.Latitude + (pair.Second.Position.Latitude - pair.First.Position.Latitude) * i / 10,
                pair.First.Position.Longitude + (pair.Second.Position.Longitude - pair.First.Position.Longitude) * i / 10), null)))
            .Append(path.Points[^1]).ToArray();
        var original = LoopGeometryMetrics.NearReturnMeters(path, Ct)!.Value;
        Assert.InRange(LoopGeometryMetrics.NearReturnMeters(path with { Points = dense }, Ct)!.Value, original - 60, original + 60);
        Assert.InRange(LoopGeometryMetrics.NearReturnMeters(path with { Points = path.Points.Reverse().ToArray() }, Ct)!.Value, original - 100, original + 100);
    }

    [Fact]
    public void FarParallelRoads_AreNotNearReturns()
    {
        Assert.Equal(0, LoopGeometryMetrics.NearReturnMeters(ParallelReturn(60), Ct));
    }

    [Fact]
    public void DenseRepeatedLaps_ExhaustWorkBudgetInsteadOfBlockingTheRequest()
    {
        var vertices = Enumerable.Range(0, 900).SelectMany(_ => new (double, double)[]
            { (0, 0), (100, 0), (100, 100), (0, 100) }).Append((0d, 0d)).ToArray();
        Assert.Null(LoopGeometryMetrics.NearReturnMeters(Meters(vertices), Ct));
    }

    [Fact]
    public void CancellationAndOversizedRoutes_AreBounded()
    {
        Assert.ThrowsAny<OperationCanceledException>(() => LoopGeometryMetrics.NearReturnMeters(ParallelReturn(), new(true)));
        Assert.Null(LoopGeometryMetrics.NearReturnMeters(Meters((0, 0), (500000, 0), (500000, 1000), (0, 0)), Ct));
    }

    internal static RoutedPath ParallelReturn(double gap = 10) => Meters((0, 0), (1000, 1000),
        (3000, 1000), (3000, 1000 + gap), (1000, 1000 + gap), (-1000, 2000), (0, 0));

    // Equatorial coordinates make these synthetic distances independent of private GPS data.
    internal static RoutedPath Meters(params (double X, double Y)[] points) =>
        RoadQualityAssessorTests.Path(points.Select(p => (p.Y / 111195, p.X / 111195)).ToArray());
}
