import type { Coordinate } from './types';

export type ProfilePoint = { index: number; distanceMeters: number; elevationMeters: number | null };

/** Invalid coordinates discard the entire path; missing/nonfinite heights remain null. */
export function buildElevationProfile(geometry: readonly (Coordinate & { elevationMeters: number | null })[]) {
  const result = { points: [] as ProfilePoint[], distanceMeters: 0, minElevationMeters: null as number | null,
    maxElevationMeters: null as number | null, knownCount: 0 };
  for (const point of geometry) {
    if (!point || !Number.isFinite(point.latitude) || !Number.isFinite(point.longitude)
      || Math.abs(point.latitude) > 90 || Math.abs(point.longitude) > 180) return result;
  }
  const radians = Math.PI / 180;
  for (let index = 0; index < geometry.length; index++) {
    const point = geometry[index];
    if (index > 0) {
      const previous = geometry[index - 1];
      const latitudeDelta = (point.latitude - previous.latitude) * radians;
      const longitudeDelta = (point.longitude - previous.longitude) * radians;
      const a = Math.sin(latitudeDelta / 2) ** 2
        + Math.cos(previous.latitude * radians) * Math.cos(point.latitude * radians) * Math.sin(longitudeDelta / 2) ** 2;
      result.distanceMeters += 6371000 * 2 * Math.asin(Math.sqrt(Math.min(1, Math.max(0, a))));
    }
    const elevationMeters = Number.isFinite(point.elevationMeters) ? point.elevationMeters : null;
    result.points.push({ index, distanceMeters: result.distanceMeters, elevationMeters });
    if (elevationMeters !== null) {
      result.knownCount++;
      result.minElevationMeters = result.minElevationMeters === null ? elevationMeters : Math.min(result.minElevationMeters, elevationMeters);
      result.maxElevationMeters = result.maxElevationMeters === null ? elevationMeters : Math.max(result.maxElevationMeters, elevationMeters);
    }
  }
  return result;
}

/**
 * Soft point budget: endpoints, global extrema, and both sides of each null boundary
 * are mandatory, even if alternating gaps exceed maximum. Remaining space retains
 * min/max heights in equal index buckets. Invalid budgets default to 1200; positive
 * budgets are floored (at least 1). Returns original point objects in track order.
 * Inputs are ordered, normalized profile points; rendering must not span nulls.
 */
export function sampleProfile(points: readonly ProfilePoint[], maximum = 1200): ProfilePoint[] {
  const budget = Number.isFinite(maximum) && maximum > 0 ? Math.max(1, Math.floor(maximum)) : 1200;
  if (points.length <= budget) return [...points];
  const selected = new Set([0, points.length - 1]);
  let minimum: number | null = null;
  let maximumIndex: number | null = null;
  for (let index = 0; index < points.length; index++) {
    const elevation = points[index].elevationMeters;
    if (index > 0 && (elevation === null) !== (points[index - 1].elevationMeters === null)) {
      selected.add(index - 1);
      selected.add(index);
    }
    if (elevation !== null) {
      if (minimum === null || elevation < points[minimum].elevationMeters!) minimum = index;
      if (maximumIndex === null || elevation > points[maximumIndex].elevationMeters!) maximumIndex = index;
    }
  }
  if (minimum !== null) selected.add(minimum);
  if (maximumIndex !== null) selected.add(maximumIndex);

  const bucketCount = Math.max(0, Math.floor((budget - selected.size) / 2));
  for (let bucket = 0; bucket < bucketCount; bucket++) {
    const start = Math.floor(bucket * points.length / bucketCount);
    const end = Math.floor((bucket + 1) * points.length / bucketCount);
    let bucketMinimum: number | null = null;
    let bucketMaximum: number | null = null;
    for (let index = start; index < end; index++) {
      const elevation = points[index].elevationMeters;
      if (elevation !== null) {
        if (bucketMinimum === null || elevation < points[bucketMinimum].elevationMeters!) bucketMinimum = index;
        if (bucketMaximum === null || elevation > points[bucketMaximum].elevationMeters!) bucketMaximum = index;
      }
    }
    if (bucketMinimum !== null) selected.add(bucketMinimum);
    if (bucketMaximum !== null) selected.add(bucketMaximum);
  }
  return [...selected].sort((a, b) => a - b).map(index => points[index]);
}

function lowerBoundDistance(points: readonly ProfilePoint[], distanceMeters: number): number {
  let low = 0;
  let high = points.length;
  while (low < high) {
    const middle = low + Math.floor((high - low) / 2);
    if (points[middle].distanceMeters < distanceMeters) low = middle + 1;
    else high = middle;
  }
  return low;
}

/** Search the full distance-ordered profile, not the sample. Ties use the earliest original index. */
export function nearestProfileIndex(points: readonly ProfilePoint[], distanceMeters: number): number | null {
  if (points.length === 0 || !Number.isFinite(distanceMeters)) return null;
  const target = Math.min(points[points.length - 1].distanceMeters, Math.max(points[0].distanceMeters, distanceMeters));
  let nearest = lowerBoundDistance(points, target);
  if (nearest > 0 && target - points[nearest - 1].distanceMeters <= points[nearest].distanceMeters - target) nearest--;
  // A left neighbor or clamped last point may be the end of a duplicate-distance run.
  return points[lowerBoundDistance(points, points[nearest].distanceMeters)].index;
}
