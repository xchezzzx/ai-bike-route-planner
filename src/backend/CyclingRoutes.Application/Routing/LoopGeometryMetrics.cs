namespace CyclingRoutes.Application.Routing;

public static class LoopGeometryMetrics
{
    private const double Step = 20;
    private const double Proximity = 25;
    private const double MinimumSeparation = 500;
    private const double MinimumRun = 300;
    private const int ComparisonBudget = 1_000_000;

    // A geometric hint, not proof of a repeated road, an illegal turn or unsuitable surface.
    // Null means the local projection/work budget cannot support this check.
    public static double? NearReturnMeters(RoutedPath path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (path.Points.Count is < 3 or > 200000) return null;
        if (RouteGeometryMetrics.DistanceMeters(path.Points[0].Position, path.Points[^1].Position) > 50) return null;
        var lengths = RouteGeometryMetrics.EdgeLengths(path.Points, cancellationToken);
        var total = lengths.Sum();
        if (!double.IsFinite(total) || total <= 0 || total > 400000) return null;
        var projected = new Point[path.Points.Count];
        var origin = path.Points[0].Position;
        const double metersPerDegree = 6371008.8 * Math.PI / 180;
        var longitudeScale = metersPerDegree * Math.Cos(origin.Latitude * Math.PI / 180);
        if (Math.Abs(origin.Latitude) > 70) return null;
        for (var i = 0; i < projected.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var p = path.Points[i].Position;
            var longitudeDelta = Math.IEEERemainder(p.Longitude - origin.Longitude, 360);
            if (Math.Abs(p.Latitude - origin.Latitude) > 2 || Math.Abs(longitudeDelta) > 2) return null;
            projected[i] = new(longitudeDelta * longitudeScale, (p.Latitude - origin.Latitude) * metersPerDegree);
        }
        var samples = Sample(projected, lengths, total, cancellationToken);
        var headings = new Point[samples.Count];
        for (var i = 0; i < samples.Count; i++)
        {
            var delta = samples[Math.Min(samples.Count - 1, i + 2)] - samples[Math.Max(0, i - 2)];
            var norm = Math.Sqrt(delta.Dot(delta));
            headings[i] = norm < 10 ? new(0, 0) : delta * (1 / norm);
        }

        // Monotone spatial alignment tolerates small unequal detours on a shared access stem.
        var outgoingStem = 0d;
        var returningStem = 0d;
        var returnIndex = 0;
        var misses = 0;
        for (var i = 1; i * Step < total / 2; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var best = double.PositiveInfinity;
            var bestIndex = returnIndex;
            for (var j = returnIndex; j <= returnIndex + 8 && j * Step < total / 2; j++)
            {
                var delta = samples[i] - At(total - j * Step);
                var distance = delta.Dot(delta);
                if (distance < best) { best = distance; bestIndex = j; }
            }
            if (best > Proximity * Proximity)
            {
                if (++misses > 5) break;
                continue;
            }
            misses = 0;
            returnIndex = bestIndex;
            outgoingStem = i * Step;
            returningStem = bestIndex * Step;
        }
        var outgoingGuard = outgoingStem + 100;
        var returningGuard = returningStem + 100;
        var cells = new Dictionary<(int X, int Y), List<int>>();
        var comparisons = 0;
        var run = 0d;
        var gap = 0d;
        var result = 0d;
        var previousMatch = -1;
        var runHeading = new Point(0, 0);
        for (var i = 0; i < samples.Count - 1; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var progress = i * Step;
            if (progress < outgoingGuard || total - progress < returningGuard) { FinishRun(); continue; }
            var p = samples[i];
            var cell = Cell(p);
            var match = -1;
            var nearest = double.PositiveInfinity;
            for (var dx = -2; dx <= 2; dx++)
                for (var dy = -2; dy <= 2; dy++)
                {
                    if (!cells.TryGetValue((cell.X + dx, cell.Y + dy), out var prior)) continue;
                    foreach (var j in prior)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++comparisons > ComparisonBudget) return null;
                        if ((i - j) * Step < MinimumSeparation || headings[i].Dot(headings[j]) > -0.8660254037844386) continue;
                        var distance = SegmentDistanceSquared(p, samples[j], samples[j + 1]);
                        if (distance > Proximity * Proximity || distance >= nearest) continue;
                        nearest = distance;
                        match = j;
                    }
                }
            if (match >= 0)
            {
                // Do not join different earlier passages or successive short hairpin legs.
                if (previousMatch >= 0 && (Math.Abs((previousMatch - match) * Step - (gap + Step)) > 100
                    || runHeading.Dot(headings[i]) < 0.5)) FinishRun();
                runHeading = headings[i];
                run += Step; gap = 0; previousMatch = match;
            }
            else if ((gap += Step) > 40) FinishRun();
            var segmentCell = Cell((samples[i] + samples[i + 1]) * 0.5);
            if (!cells.TryGetValue(segmentCell, out var bucket)) cells[segmentCell] = bucket = [];
            bucket.Add(i);
        }
        FinishRun();
        return Math.Min(result, total);

        void FinishRun()
        {
            if (run >= MinimumRun) result += run;
            run = 0; gap = 0; previousMatch = -1;
        }
        Point At(double progress)
        {
            var index = Math.Min(samples.Count - 2, (int)(progress / Step));
            var span = Math.Min(Step, total - index * Step);
            return samples[index] + (samples[index + 1] - samples[index]) * ((progress - index * Step) / span);
        }
    }

    private static double SegmentDistanceSquared(Point point, Point a, Point b)
    {
        var edge = b - a;
        var lengthSquared = edge.Dot(edge);
        var fraction = lengthSquared == 0 ? 0 : Math.Clamp((point - a).Dot(edge) / lengthSquared, 0, 1);
        var delta = point - (a + edge * fraction);
        return delta.Dot(delta);
    }

    private static List<Point> Sample(Point[] points, double[] lengths, double total, CancellationToken ct)
    {
        var samples = new List<Point>((int)(total / Step) + 2);
        var edge = 0;
        var edgeStart = 0d;
        for (var progress = 0d; progress < total; progress += Step)
        {
            ct.ThrowIfCancellationRequested();
            while (edge < lengths.Length - 1 && edgeStart + lengths[edge] <= progress)
                edgeStart += lengths[edge++];
            var fraction = lengths[edge] == 0 ? 0 : (progress - edgeStart) / lengths[edge];
            samples.Add(points[edge] + (points[edge + 1] - points[edge]) * fraction);
        }
        samples.Add(points[^1]);
        return samples;
    }

    private static (int X, int Y) Cell(Point p) => ((int)Math.Floor(p.X / Proximity), (int)Math.Floor(p.Y / Proximity));
    private readonly record struct Point(double X, double Y)
    {
        public double Dot(Point other) => X * other.X + Y * other.Y;
        public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
        public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
        public static Point operator *(Point a, double scale) => new(a.X * scale, a.Y * scale);
    }
}
