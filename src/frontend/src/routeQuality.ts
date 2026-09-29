import type { Candidates, RoadQuality } from './types';

const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const number = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v);
const nonnegative = (v: unknown): v is number => number(v) && v >= 0;
const strings = (v: unknown): v is string[] => Array.isArray(v) && v.every(x => typeof x === 'string');
const surfaceKeys = ['pavedMeters', 'nonRoadMeters', 'otherKnownMeters', 'unknownMeters'];
const wayKeys = ['unknownMeters', 'stateRoadMeters', 'roadMeters', 'streetMeters', 'pathMeters', 'trackMeters', 'cyclewayMeters', 'footwayMeters', 'stepsMeters', 'ferryMeters', 'constructionMeters'];

function quality(value: unknown): value is RoadQuality {
  if (!object(value) || value.policyVersion !== 'road-v1' || !number(value.geometryLengthMeters) || value.geometryLengthMeters <= 0
    || typeof value.surfaceEvidenceState !== 'string' || !['unavailable', 'partial', 'complete'].includes(value.surfaceEvidenceState) || typeof value.waytypeSupplied !== 'boolean') return false;
  const length = value.geometryLengthMeters;
  const close = (a: number, b: number) => Math.abs(a - b) <= length * 1e-8;
  function partition(part: unknown, keys: string[]) {
    return object(part) && keys.every(key => nonnegative(part[key])) && close(keys.reduce((sum, key) => sum + (part[key] as number), 0), length);
  }
  if (!partition(value.surface, surfaceKeys) || !partition(value.ways, wayKeys)) return false;
  const unknown = (value.surface as Record<string, number>).unknownMeters;
  if (value.surfaceEvidenceState === 'complete' && unknown !== 0 || value.surfaceEvidenceState === 'partial' && unknown <= 0
    || value.surfaceEvidenceState === 'unavailable' && !close(unknown, length)
    || !value.waytypeSupplied && !close((value.ways as Record<string, number>).unknownMeters, length)) return false;
  return nonnegative(value.repeatedMeters) && nonnegative(value.sharedStemMeters) && nonnegative(value.remainingRepeatedMeters)
    && value.repeatedMeters <= length * (1 + 1e-8) && value.sharedStemMeters <= value.repeatedMeters
    && close(value.repeatedMeters - value.sharedStemMeters, value.remainingRepeatedMeters);
}

export function validRoadCandidates(value: unknown): value is Candidates {
  if (!object(value) || !Array.isArray(value.candidates) || !Array.isArray(value.excludedCandidates)
    || !strings(value.warnings) || !strings(value.assumptions) || !number(value.requestedLengthMeters)
    || value.requestedLengthMeters < 1000 || value.requestedLengthMeters > 100000
    || !number(value.attemptedCount) || !Number.isInteger(value.attemptedCount) || value.attemptedCount < 1 || value.attemptedCount > 3) return false;
  const count = value.candidates.length + value.excludedCandidates.length;
  if (count < 1 || count > value.attemptedCount || (!value.candidates.length && !value.warnings.includes('no_candidate_meets_requirements'))) return false;
  const seeds = new Set<number>();
  function candidate(item: unknown, excluded: boolean) {
    if (!object(item) || !number(item.seed) || !Number.isInteger(item.seed) || item.seed < 1 || item.seed > 16 || seeds.has(item.seed)
      || !object(item.assessment)) return false;
    seeds.add(item.seed);
    const a = item.assessment;
    if (typeof a.targetsMatched !== 'boolean' || !nonnegative(a.score) || !quality(a.quality)
      || !(a.distanceDeltaMeters === null || number(a.distanceDeltaMeters)) || !(a.durationDeltaSeconds === null || number(a.durationDeltaSeconds))) return false;
    const q = a.quality;
    const reasons = [
      ...(!a.targetsMatched ? ['targets_not_met'] : []),
      ...(q.surface.nonRoadMeters > Math.max(100, .005 * q.geometryLengthMeters) ? ['road_surface_limit_exceeded'] : []),
      ...(q.ways.stepsMeters > 0 || q.ways.ferryMeters > 0 || q.ways.constructionMeters > 0 ? ['road_waytype_excluded'] : []),
    ];
    if (excluded) return number(item.distanceMeters) && item.distanceMeters > 0 && number(item.estimatedDurationSeconds) && item.estimatedDurationSeconds > 0
      && strings(item.reasons) && reasons.length > 0 && item.reasons.length === reasons.length && reasons.every(r => (item.reasons as string[]).includes(r));
    if (reasons.length || !object(item.route)) return false;
    const r = item.route;
    return number(r.distanceMeters) && r.distanceMeters > 0 && number(r.estimatedDurationSeconds) && r.estimatedDurationSeconds > 0
      && Array.isArray(r.geometry) && r.geometry.length >= 4 && r.geometry.every(p => object(p) && number(p.latitude) && Math.abs(p.latitude) <= 90 && number(p.longitude) && Math.abs(p.longitude) <= 180)
      && r.geometry[0].latitude === r.geometry.at(-1).latitude && r.geometry[0].longitude === r.geometry.at(-1).longitude
      && strings(r.warnings) && typeof r.attribution === 'string' && typeof r.gpx === 'string' && r.gpx.length > 0
      && (r.ascentMeters === null || nonnegative(r.ascentMeters)) && (r.descentMeters === null || nonnegative(r.descentMeters));
  }
  return value.candidates.every(c => candidate(c, false)) && value.excludedCandidates.every(c => candidate(c, true));
}
