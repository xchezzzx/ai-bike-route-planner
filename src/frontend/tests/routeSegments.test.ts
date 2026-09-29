import { describe, expect, it } from 'vitest';
import { readRouteSegments, segmentFeatures } from '../src/routeSegments';
import { route } from './fixtures';

const unknown = [{ fromPointIndex: 0, toPointIndex: 3, surface: 'unknown', wayType: 'unknown' }];
const first = { fromPointIndex: 0, toPointIndex: 1, surface: 'asphalt', wayType: 'cycleway' };
const last = { fromPointIndex: 1, toPointIndex: 3, surface: 'unpaved', wayType: 'track' };

describe('optional segment metadata', () => {
  it.each([undefined, null, []])('keeps legacy routes usable (%j)', value => {
    expect(readRouteSegments(value, 4)).toEqual({ segments: unknown, status: 'missing' });
  });
  it.each([
    {}, 'bad', [null], [first], [{ ...first, fromPointIndex: -1 }, last],
    [{ ...first, toPointIndex: 1.5 }, last], [first, { ...last, toPointIndex: 4 }],
    [first, { ...last, fromPointIndex: 2 }], [first, { ...last, fromPointIndex: 0 }],
    [{ ...first, surface: 'safeAsphalt' }, last], [first, { ...last, wayType: 'permitted' }],
    [first, { ...last, toPointIndex: 1 }], [first, first, first, last],
  ])('discards malformed partitions as a whole (%j)', value => {
    expect(readRouteSegments(value, 4)).toEqual({ segments: unknown, status: 'invalid' });
  });
  it('slices inclusive endpoints without mutation', () => {
    const input = Object.freeze([Object.freeze(first), Object.freeze(last)]);
    const parsed = readRouteSegments(input, 4);
    expect(parsed.status).toBe('valid');
    const original = JSON.stringify(route);
    const features = segmentFeatures(route, parsed.segments, 2, '#346db5').features;
    expect(features.map(f => f.geometry.coordinates)).toEqual([
      [[34.78, 32.08], [34.8, 32.11]],
      [[34.8, 32.11], [34.83, 32.09], [34.78, 32.08]],
    ]);
    expect(features[1].properties).toMatchObject({ segmentIndex: 1, candidateIndex: 2, color: '#346db5', surface: 'unpaved', wayType: 'track' });
    expect(JSON.stringify(route)).toBe(original);
  });
  it('does not renumber duplicate coordinates', () => {
    const duplicate = { ...route, geometry: [route.geometry[0], route.geometry[0], ...route.geometry.slice(2)] };
    const segments = readRouteSegments([first, last], 4).segments;
    expect(segmentFeatures(duplicate, segments, 0, 'red').features[0].geometry.coordinates).toEqual([[34.78, 32.08], [34.78, 32.08]]);
  });
});
