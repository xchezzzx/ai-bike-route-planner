import { describe, expect, it } from 'vitest';
import { buildElevationProfile, nearestProfileIndex, sampleProfile } from '../src/elevationProfile';
import type { ProfilePoint } from '../src/elevationProfile';
import type { GeneratedRoute } from '../src/types';

type Geometry = GeneratedRoute['geometry'];
const emptyProfile = { points: [], distanceMeters: 0, minElevationMeters: null, maxElevationMeters: null, knownCount: 0 };
const coordinate = (longitude: number, elevationMeters: number | null = null, latitude = 0): Geometry[number] =>
  ({ latitude, longitude, elevationMeters });
const pointsWith = (elevations: readonly (number | null)[]): ProfilePoint[] =>
  elevations.map((elevationMeters, index) => ({ index, distanceMeters: index * 10, elevationMeters }));

describe('buildElevationProfile', () => {
  it('returns empty statistics for empty geometry', () => {
    expect(buildElevationProfile([])).toEqual(emptyProfile);
  });

  it.each([0, -430, 123, null])('keeps a single point with elevation %s at distance zero', elevationMeters => {
    expect(buildElevationProfile([coordinate(34, elevationMeters, 32)])).toEqual({
      points: [{ index: 0, distanceMeters: 0, elevationMeters }], distanceMeters: 0,
      minElevationMeters: elevationMeters, maxElevationMeters: elevationMeters, knownCount: elevationMeters === null ? 0 : 1,
    });
  });

  it('accumulates horizontal Haversine distance in meters', () => {
    const result = buildElevationProfile([coordinate(0, 0), coordinate(1, 1000), coordinate(2, -1000)]);
    expect(result.points.map(point => point.index)).toEqual([0, 1, 2]);
    expect(result.points[0].distanceMeters).toBe(0);
    expect(result.points[1].distanceMeters).toBeCloseTo(111194.926645, 5);
    expect(result.points[2].distanceMeters).toBeCloseTo(222389.853289, 5);
    expect(result.distanceMeters).toBe(result.points[2].distanceMeters);
    expect(result).toMatchObject({ minElevationMeters: -1000, maxElevationMeters: 1000, knownCount: 3 });
  });

  it('accounts for latitude when measuring east-west travel', () => {
    expect(buildElevationProfile([coordinate(0, null, 60), coordinate(1, null, 60)]).distanceMeters)
      .toBeCloseTo(55596.934071, 5);
  });

  it('preserves loops, retracing, and duplicate consecutive coordinates', () => {
    const result = buildElevationProfile([
      coordinate(0, 0), coordinate(1, 10), coordinate(1, 20), coordinate(0, null), coordinate(1, 30),
    ]);
    expect(result.points.map(point => point.index)).toEqual([0, 1, 2, 3, 4]);
    expect(result.points.map(point => point.elevationMeters)).toEqual([0, 10, 20, null, 30]);
    expect(result.points[2].distanceMeters).toBe(result.points[1].distanceMeters);
    expect(result.points[3].distanceMeters).toBeCloseTo(222389.853289, 5);
    expect(result.distanceMeters).toBeCloseTo(333584.779934, 5);
  });

  it('keeps nonfinite and absent elevations as null, not zero', () => {
    const geometry = [0, -430, null, NaN, Infinity, -Infinity, undefined, '12', 17]
      .map((elevationMeters, index) => ({ latitude: 0, longitude: index, elevationMeters })) as unknown as Geometry;
    const result = buildElevationProfile(geometry);
    expect(result.points.map(point => point.elevationMeters)).toEqual([0, -430, null, null, null, null, null, null, 17]);
    expect(result).toMatchObject({ minElevationMeters: -430, maxElevationMeters: 17, knownCount: 3 });
    expect(result.distanceMeters).toBeCloseTo(889559.413156, 5);
  });

  it('keeps statistics unknown for an entirely missing profile while retaining path distance', () => {
    const result = buildElevationProfile([coordinate(0), coordinate(1), coordinate(2)]);
    expect(result.points.map(point => point.elevationMeters)).toEqual([null, null, null]);
    expect(result).toMatchObject({ minElevationMeters: null, maxElevationMeters: null, knownCount: 0 });
    expect(result.distanceMeters).toBeCloseTo(222389.853289, 5);
  });

  it.each([
    { latitude: NaN, longitude: 0 }, { latitude: Infinity, longitude: 0 }, { latitude: -Infinity, longitude: 0 },
    { latitude: 0, longitude: NaN }, { latitude: 0, longitude: Infinity }, { latitude: 0, longitude: -Infinity },
    { latitude: 90.0001, longitude: 0 }, { latitude: -90.0001, longitude: 0 },
    { latitude: 0, longitude: 180.0001 }, { latitude: 0, longitude: -180.0001 },
    { latitude: '0', longitude: 0 }, { latitude: 0, longitude: '0' },
    { latitude: null, longitude: 0 }, { longitude: 0 }, { latitude: 0 }, {}, null, undefined,
  ])('rejects the entire path for malformed coordinate %j', invalid => {
    const geometry = [coordinate(0, 1), invalid, coordinate(2, 2)] as unknown as Geometry;
    expect(buildElevationProfile(geometry)).toEqual(emptyProfile);
  });

  it('rejects a sparse coordinate array rather than skipping its hole', () => {
    const geometry = [coordinate(0), coordinate(1), coordinate(2)];
    delete geometry[1];
    expect(buildElevationProfile(geometry)).toEqual(emptyProfile);
  });

  it('accepts coordinate bounds and returns finite distances for antipodal points', () => {
    const result = buildElevationProfile([coordinate(-180, 0, -90), coordinate(180, 1, 90)]);
    expect(result.points).toHaveLength(2);
    expect(result.distanceMeters).toBeCloseTo(20015086.796021, 5);
  });

  it('takes the short distance across the antimeridian', () => {
    expect(buildElevationProfile([coordinate(179.9), coordinate(-179.9)]).distanceMeters)
      .toBeCloseTo(22238.985329, 5);
  });

  it('does not mutate frozen geometry or share result arrays between calls', () => {
    const geometry = Object.freeze([Object.freeze(coordinate(0, -2)), Object.freeze(coordinate(1, 0))]);
    const first = buildElevationProfile(geometry);
    const second = buildElevationProfile(geometry);
    expect(first).toEqual(second);
    expect(first.points).not.toBe(second.points);
    expect(first.points[0]).not.toBe(second.points[0]);
    expect(geometry[0]).toEqual(coordinate(0, -2));
  });
});

describe('sampleProfile', () => {
  it('handles empty and single-point profiles', () => {
    expect(sampleProfile([])).toEqual([]);
    expect(sampleProfile(pointsWith([null]), 1)).toEqual(pointsWith([null]));
  });

  it('retains all points below the budget without mutating its input', () => {
    const points = Object.freeze(pointsWith([0, -10, null, 30]).map(point => Object.freeze(point)));
    const sampled = sampleProfile(points);
    expect(sampled).toEqual(points);
    expect(sampled).not.toBe(points);
  });

  it('preserves first, last, and global extrema even when they exceed a tiny budget', () => {
    const points = pointsWith([5, 10, -40, 12, 90, 8]);
    const sampled = sampleProfile(points, 1);
    expect(sampled.map(point => point.index)).toEqual([0, 2, 4, 5]);
  });

  it('retains local bucket valleys and peaks as well as the global extrema', () => {
    const elevations = Array<number>(40).fill(0);
    elevations[7] = -50;
    elevations[9] = 80;
    elevations[27] = -10;
    elevations[29] = 30;
    const sampled = sampleProfile(pointsWith(elevations), 8);
    expect(sampled.map(point => point.index)).toEqual(expect.arrayContaining([0, 7, 9, 27, 29, 39]));
    expect(sampled.length).toBeLessThanOrEqual(8);
  });

  it('retains both sides of every missing-data boundary and every known run edge', () => {
    const elevations = Array<number | null>(1000).fill(10);
    for (const index of [0, 1, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 400, 998, 999]) elevations[index] = null;
    const points = pointsWith(elevations);
    const sampled = sampleProfile(points, 12);
    expect(sampled.map(point => point.index)).toEqual(expect.arrayContaining([
      0, 1, 2, 199, 200, 209, 210, 399, 400, 401, 997, 998, 999,
    ]));
    for (let index = 1; index < sampled.length; index++) {
      const before = sampled[index - 1];
      const after = sampled[index];
      if (before.elevationMeters !== null && after.elevationMeters !== null) {
        expect(points.slice(before.index + 1, after.index).some(point => point.elevationMeters === null)).toBe(false);
      }
    }
  });

  it('keeps every alternating null gap even when this greatly exceeds the budget', () => {
    const points = pointsWith(Array.from({ length: 5000 }, (_, index) => index % 2 === 0 ? index : null));
    expect(sampleProfile(points, 12)).toEqual(points);
  });

  it('keeps endpoints of an all-null run without inventing elevations', () => {
    const sampled = sampleProfile(pointsWith(Array<null>(5000).fill(null)));
    expect(sampled.map(point => point.index)).toEqual([0, 4999]);
    expect(sampled.every(point => point.elevationMeters === null)).toBe(true);
  });

  it('preserves singleton known runs among missing data', () => {
    const elevations = Array<number | null>(100).fill(null);
    elevations[30] = 0;
    elevations[70] = -2;
    expect(sampleProfile(pointsWith(elevations), 2).map(point => point.index)).toEqual([0, 29, 30, 31, 69, 70, 71, 99]);
  });

  it('keeps original indices and distances in strictly increasing index order', () => {
    const points = pointsWith(Array.from({ length: 100 }, (_, index) => index % 11))
      .map(point => ({ ...point, index: point.index * 3 + 11 }));
    const sampled = sampleProfile(points, 12);
    expect(sampled[0]).toBe(points[0]);
    expect(sampled.at(-1)).toBe(points.at(-1));
    for (let index = 0; index < sampled.length; index++) {
      expect(sampled[index]).toBe(points[(sampled[index].index - 11) / 3]);
      if (index > 0) expect(sampled[index].index).toBeGreaterThan(sampled[index - 1].index);
    }
    expect(sampleProfile(points, 12)).toEqual(sampled);
  });

  it.each([NaN, Infinity, -Infinity, 0, -1])('falls back to the default budget for invalid maximum %s', maximum => {
    const points = pointsWith(Array.from({ length: 2000 }, (_, index) => index % 13));
    expect(sampleProfile(points, maximum)).toEqual(sampleProfile(points));
  });

  it('floors a positive fractional budget', () => {
    const points = pointsWith(Array.from({ length: 100 }, (_, index) => index % 7));
    expect(sampleProfile(points, 12.9)).toEqual(sampleProfile(points, 12));
  });

  it('handles 50000 geometry points with the default budget and original index mapping', () => {
    const geometry = Array.from({ length: 50000 }, (_, index) => coordinate(index / 1000, index % 101));
    geometry[12345].elevationMeters = -800;
    geometry[45678].elevationMeters = 900;
    const profile = buildElevationProfile(geometry);
    expect(profile.points).toHaveLength(50000);
    expect(profile).toMatchObject({ minElevationMeters: -800, maxElevationMeters: 900, knownCount: 50000 });
    expect(profile.distanceMeters).toBeCloseTo(5559635.137301, 4);
    const sampled = sampleProfile(profile.points);
    expect(sampled.length).toBeGreaterThan(2);
    expect(sampled.length).toBeLessThanOrEqual(1200);
    expect(sampled.map(point => point.index)).toEqual(expect.arrayContaining([0, 12345, 45678, 49999]));
    for (let index = 0; index < sampled.length; index++) {
      expect(sampled[index]).toBe(profile.points[sampled[index].index]);
      if (index > 0) expect(sampled[index].index).toBeGreaterThan(sampled[index - 1].index);
    }
    expect(nearestProfileIndex(profile.points, profile.points[23456].distanceMeters)).toBe(23456);
  });
});

describe('nearestProfileIndex', () => {
  it('returns null for an empty profile', () => {
    expect(nearestProfileIndex([], 0)).toBeNull();
  });

  it.each([NaN, Infinity, -Infinity])('rejects nonfinite query distance %s', distance => {
    expect(nearestProfileIndex(pointsWith([0, 1]), distance)).toBeNull();
  });

  it.each([
    [-Number.MAX_VALUE, 0], [-1, 0], [0, 0], [4, 0], [5, 0], [6, 1],
    [10, 1], [14, 1], [15, 1], [16, 2], [20, 2], [21, 2], [Number.MAX_VALUE, 2],
  ])('clamps finite distance %s and chooses earliest index on ties (%s)', (distance, expected) => {
    expect(nearestProfileIndex(pointsWith([0, null, -3]), distance)).toBe(expected);
  });

  it('returns the original index rather than the sampled array position', () => {
    const points = [{ index: 11, distanceMeters: 100, elevationMeters: 1 }, { index: 71, distanceMeters: 200, elevationMeters: 2 }];
    expect(nearestProfileIndex(points, -1)).toBe(11);
    expect(nearestProfileIndex(points, 150)).toBe(11);
    expect(nearestProfileIndex(points, 190)).toBe(71);
  });

  it.each([[0, 0], [1, 0], [5, 0], [6, 2], [10, 2], [14, 2], [15, 2], [16, 4], [20, 4], [30, 4]])(
    'selects the earliest duplicate on both exact and nearby distances (%s)', (distance, expected) => {
      const points = [0, 0, 10, 10, 20, 20].map((distanceMeters, index) => ({ index, distanceMeters, elevationMeters: null }));
      expect(nearestProfileIndex(points, distance)).toBe(expected);
    },
  );

  it('returns the first index when every coordinate has the same distance', () => {
    const points = [11, 12, 13].map(index => ({ index, distanceMeters: 0, elevationMeters: null }));
    expect(nearestProfileIndex(points, -10)).toBe(11);
    expect(nearestProfileIndex(points, 0)).toBe(11);
    expect(nearestProfileIndex(points, 10)).toBe(11);
  });

  it('matches an independent nearest-distance scan for a long duplicate-heavy profile', () => {
    const points = Array.from({ length: 50000 }, (_, index) => ({ index, distanceMeters: Math.floor(index / 4) * 3, elevationMeters: null }));
    for (const query of [-10, 0, 1.5, 4, 500.25, 12345, 30000.5, 37497, 50000]) {
      let expected = 0;
      for (let index = 1; index < points.length; index++) {
        if (Math.abs(points[index].distanceMeters - query) < Math.abs(points[expected].distanceMeters - query)) expected = index;
      }
      expect(nearestProfileIndex(points, query)).toBe(expected);
    }
  });
});
