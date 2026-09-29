import { expect, it } from 'vitest';
import { segmentPattern } from '../src/routeSegmentStyle';
import type { RouteSegment, RouteSurface, RouteWayType } from '../src/types';

const base: RouteSegment = { fromPointIndex: 0, toPointIndex: 1, surface: 'asphalt', wayType: 'road' };
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
