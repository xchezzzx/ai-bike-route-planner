import type { LineLayerSpecification } from 'maplibre-gl';
import type { RouteSegment } from './types';

export type SegmentDisplayMode = 'surface' | 'wayType';
export interface SegmentPattern { dashArray: readonly number[] | null; centerStripe: boolean; caution: boolean }
const patterns: Record<string, SegmentPattern> = {
  solid: { dashArray: null, centerStripe: false, caution: false },
  striped: { dashArray: null, centerStripe: true, caution: false },
  short: { dashArray: [2, 2], centerStripe: false, caution: false },
  mixed: { dashArray: [4, 2, 1, 2], centerStripe: false, caution: false },
  dots: { dashArray: [0.5, 2], centerStripe: false, caution: false },
  long: { dashArray: [6, 2], centerStripe: false, caution: false },
  paired: { dashArray: [2, 1, 2, 3], centerStripe: false, caution: false },
  caution: { dashArray: [1, 1], centerStripe: false, caution: true },
};
export function segmentPatternKey(segment: RouteSegment, mode: SegmentDisplayMode): string {
  if (mode === 'surface') return { asphalt: 'solid', paved: 'striped', unpaved: 'short', other: 'mixed', unknown: 'dots' }[segment.surface];
  return { stateRoad: 'solid', road: 'solid', street: 'solid', cycleway: 'long', footway: 'paired', path: 'short', track: 'short',
    steps: 'caution', ferry: 'caution', construction: 'caution', unknown: 'dots' }[segment.wayType];
}
export function segmentPattern(segment: RouteSegment, mode: SegmentDisplayMode): SegmentPattern { return patterns[segmentPatternKey(segment, mode)]; }

export function segmentLayers(): LineLayerSpecification[] {
  const base = { type: 'line' as const, source: 'track-segments', layout: { 'line-cap': 'butt' as const, 'line-join': 'round' as const } };
  return [
    { ...base, id: 'segment-outline', paint: { 'line-color': '#43534d', 'line-width': 8, 'line-opacity': 0.65 } },
    { ...base, id: 'segment-backing', paint: { 'line-color': '#ffffff', 'line-width': 6 } },
    ...Object.entries(patterns).map(([key, pattern]): LineLayerSpecification => ({ ...base, id: `segment-${key}`,
      filter: ['==', ['get', 'pattern'], key], paint: { 'line-color': ['get', 'color'], 'line-width': 5,
        ...(pattern.dashArray ? { 'line-dasharray': [...pattern.dashArray] } : {}),
      },
    })),
    { ...base, id: 'segment-stripe', filter: ['==', ['get', 'pattern'], 'striped'], paint: { 'line-color': '#ffffff', 'line-width': 1.2 } },
    { ...base, id: 'segment-caution-stripe', filter: ['==', ['get', 'pattern'], 'caution'], paint: { 'line-color': '#e29d22', 'line-width': 2 } },
    { ...base, id: 'segment-hit', paint: { 'line-color': '#000000', 'line-width': 16, 'line-opacity': 0.01 } },
  ];
}
