using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

public sealed class RoadQualityAssessor
{
	public RoadQualityAssessment Assess(RoutedPath path, CancellationToken cancellationToken)
	{
		var edges = RouteGeometryMetrics.EdgeLengths(path.Points, cancellationToken);
		var length = edges.Sum();
		if (!double.IsFinite(length) || length <= 0) throw Invalid();
		var evidence = path.Evidence ?? new(length, false, false, new(0, 0, 0, length), new(length, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
		var s = evidence.Surface; var w = evidence.Ways;
		Check([s.PavedMeters, s.NonRoadMeters, s.OtherKnownMeters, s.UnknownMeters]);
		Check([w.UnknownMeters, w.StateRoadMeters, w.RoadMeters, w.StreetMeters, w.PathMeters, w.TrackMeters,
			w.CyclewayMeters, w.FootwayMeters, w.StepsMeters, w.FerryMeters, w.ConstructionMeters]);
		if (!double.IsFinite(evidence.GeometryLengthMeters) || Math.Abs(evidence.GeometryLengthMeters - length) > length * 1e-8
			|| !evidence.SurfaceSupplied && s.UnknownMeters < length * (1 - 1e-8)
			|| !evidence.WaytypeSupplied && w.UnknownMeters < length * (1 - 1e-8)) throw Invalid();
		var seen = new HashSet<(GeoCoordinate, GeoCoordinate)>();
		var repeated = 0d;
		for (var i = 0; i < edges.Length; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (edges[i] == 0) continue;
			var a = path.Points[i].Position; var b = path.Points[i + 1].Position;
			var key = a.Latitude < b.Latitude || a.Latitude == b.Latitude && a.Longitude <= b.Longitude ? (a, b) : (b, a);
			if (!seen.Add(key)) repeated += edges[i];
		}
		var shared = 0d;
		for (var i = 0; i < edges.Length / 2; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (path.Points[i].Position != path.Points[^(i + 1)].Position || path.Points[i + 1].Position != path.Points[^(i + 2)].Position) break;
			shared += edges[i];
		}
		shared = Math.Min(shared, repeated);
		return new("road-v1", length, !evidence.SurfaceSupplied ? SurfaceEvidenceState.Unavailable : s.UnknownMeters > 0 ? SurfaceEvidenceState.Partial : SurfaceEvidenceState.Complete,
			evidence.WaytypeSupplied, s, w, repeated, shared, Math.Max(0, repeated - shared));

		void Check(double[] values)
		{
			if (values.Any(x => !double.IsFinite(x) || x < 0) || !double.IsFinite(values.Sum()) || Math.Abs(values.Sum() - length) > length * 1e-8) throw Invalid();
		}
	}

	private static RoutingException Invalid() => new(RoutingFailure.InvalidResponse);
}
