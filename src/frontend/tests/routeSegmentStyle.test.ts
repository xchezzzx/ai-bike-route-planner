import { expect, it } from 'vitest';
import { patternFeatures, segmentPattern } from '../src/routeSegmentStyle';
import type { RouteSegment, RouteSurface, RouteWayType } from '../src/types';
import { route } from './fixtures';
import { segmentFeatures } from '../src/routeSegments';

const base: RouteSegment = { fromPointIndex: 0, toPointIndex: 1, surface: 'asphalt', wayType: 'road' };
it.each(['surface', 'wayType'] as const)('coalesces visual runs in %s without merging inspection intervals', mode => {
  const segments: RouteSegment[] = [
    { ...base, surface: 'unpaved', wayType: 'cycleway' },
    { ...base, fromPointIndex: 1, toPointIndex: 2, surface: mode === 'surface' ? 'unpaved' : 'asphalt', wayType: mode === 'wayType' ? 'cycleway' : 'road' },
    { ...base, fromPointIndex: 2, toPointIndex: 3 },
  ];
  const original = JSON.stringify({ route, segments });
  const visual = patternFeatures(route, segments, mode, 'green');
  expect(visual.features).toHaveLength(2);
  expect(visual.features[0].geometry.coordinates).toEqual(route.geometry.slice(0, 3).map(p => [p.longitude, p.latitude]));
  expect(visual.features[1].geometry.coordinates).toEqual(route.geometry.slice(2).map(p => [p.longitude, p.latitude]));
  expect(visual.features.map(f => f.properties?.pattern)).toEqual([mode === 'surface' ? 'short' : 'long', 'solid']);
  const hits = segmentFeatures(route, segments, 0, 'green');
  expect(hits.features).toHaveLength(3);
  expect(hits.features[1].properties).toMatchObject({ segmentIndex: 1, surface: segments[1].surface, wayType: segments[1].wayType });
  expect(JSON.stringify({ route, segments })).toBe(original);
});
it('distinguishes asphalt, generic paved and unverified surface', () => {
  const patterns = (['asphalt', 'paved', 'unpaved', 'other', 'unknown'] as RouteSurface[]).map(surface => segmentPattern({ ...base, surface }, 'surface'));
  expect(patterns[0]).toEqual({ dashArray: null, centerStripe: false, caution: false });
  expect(patterns[1].centerStripe).toBe(true);
  expect(new Set(patterns.map(p => JSON.stringify(p))).size).toBe(5);
  expect(patterns[2].dashArray).toEqual([2, 2]);
});
it('keeps roads continuous and other mapped ways distinguishable', () => {
  for (const wayType of ['stateRoad', 'road', 'street'] as RouteWayType[]) expect(segmentPattern({ ...base, wayType }, 'wayType').dashArray).toBeNull();
  const cycle = segmentPattern({ ...base, wayType: 'cycleway' }, 'wayType');
  const foot = segmentPattern({ ...base, wayType: 'footway' }, 'wayType');
  expect(cycle.dashArray).toEqual([6, 2]);
  expect(foot.dashArray).not.toEqual(cycle.dashArray);
  for (const wayType of ['steps', 'ferry', 'construction'] as RouteWayType[]) expect(segmentPattern({ ...base, wayType }, 'wayType').caution).toBe(true);
});
