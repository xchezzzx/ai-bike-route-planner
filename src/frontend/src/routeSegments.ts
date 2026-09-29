import type { FeatureCollection, LineString } from 'geojson';
import type { GeneratedRoute, RouteSegment } from './types';

export interface SegmentData { segments: RouteSegment[]; status: 'valid' | 'missing' | 'invalid' }
const surfaces = new Set(['asphalt', 'paved', 'unpaved', 'other', 'unknown']);
const ways = new Set(['stateRoad', 'road', 'street', 'path', 'track', 'cycleway', 'footway', 'steps', 'ferry', 'construction', 'unknown']);

export function readRouteSegments(value: unknown, pointCount: number): SegmentData {
  const fallback = (status: 'missing' | 'invalid'): SegmentData => ({ status,
    segments: pointCount >= 2 ? [{ fromPointIndex: 0, toPointIndex: pointCount - 1, surface: 'unknown', wayType: 'unknown' }] : [],
  });
  if (value == null || Array.isArray(value) && value.length === 0) return fallback('missing');
  if (!Array.isArray(value) || value.length > pointCount - 1) return fallback('invalid');
  const segments: RouteSegment[] = [];
  let cursor = 0;
  for (const item of value) {
    if (!item || typeof item !== 'object' || !Number.isInteger(item.fromPointIndex) || !Number.isInteger(item.toPointIndex)
      || item.fromPointIndex !== cursor || item.toPointIndex <= cursor || item.toPointIndex >= pointCount
      || !surfaces.has(item.surface) || !ways.has(item.wayType)) return fallback('invalid');
    segments.push({ fromPointIndex: item.fromPointIndex, toPointIndex: item.toPointIndex, surface: item.surface, wayType: item.wayType });
    cursor = item.toPointIndex;
  }
  return cursor === pointCount - 1 ? { segments, status: 'valid' } : fallback('invalid');
}

export function segmentFeatures(route: GeneratedRoute, segments: RouteSegment[], candidateIndex: number, color: string): FeatureCollection<LineString> {
  return { type: 'FeatureCollection', features: segments.map((segment, segmentIndex) => ({
    type: 'Feature', properties: { segmentIndex, candidateIndex, color, surface: segment.surface, wayType: segment.wayType },
    geometry: { type: 'LineString', coordinates: route.geometry.slice(segment.fromPointIndex, segment.toPointIndex + 1).map(p => [p.longitude, p.latitude]) },
  })) };
}
