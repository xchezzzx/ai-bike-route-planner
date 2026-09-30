import { ApiError } from './api';
import type { Coordinate, CoordinateInput, Draft, Intent, ManualInput, TargetRange } from './types';

function invalid(field: string, code: string): never { throw new ApiError('validation_failed', { [field]: [code] }); }

export function readCoordinate(input: CoordinateInput, field: string): Coordinate | undefined {
  if (!input.latitude.trim() && !input.longitude.trim()) return undefined;
  const read = (key: 'latitude' | 'longitude', limit: number) => {
    if (!input[key].trim()) return invalid(`${field}.${key}`, 'required');
    const number = Number(input[key]);
    if (!Number.isFinite(number) || Math.abs(number) > limit) return invalid(`${field}.${key}`, 'out_of_range');
    return number;
  };
  return { latitude: read('latitude', 90), longitude: read('longitude', 180) };
}

export function buildManual(form: ManualInput, start?: Coordinate, destination?: Coordinate): Intent {
  if (!start) return invalid('start', 'required');
  if (form.shape === 'loop' && destination) return invalid('destination', 'destination_not_allowed');
  if (form.shape === 'pointToPoint' && !destination) return invalid('destination', 'required');
  if (destination && destination.latitude === start.latitude && destination.longitude === start.longitude) return invalid('destination', 'must_differ_from_start');
  if (!['loop', 'pointToPoint'].includes(form.shape)) return invalid('shape', 'invalid_value');
  if (!['road', 'gravel'].includes(form.profile)) return invalid('profile', 'invalid_value');
  if (!['balanced', 'minimize', 'seekClimbs'].includes(form.elevation)) return invalid('elevation', 'invalid_value');
  const target = (text: string, scale: number, field: string) => {
    if (!text.trim()) return undefined;
    const value = Number(text) * scale;
    if (!Number.isFinite(value)) return invalid(field, 'out_of_range');
    if (value <= 0) return invalid(field, 'must_be_positive');
    if (field.startsWith('targetDuration')) {
      // Decimal minutes can produce a value one floating-point step from an integer.
      const seconds = Math.round(value);
      const roundoff = Number.EPSILON * Math.abs(value);
      if (!Number.isSafeInteger(seconds) || seconds <= 0 || seconds > 922337203685 || Math.abs(value - seconds) > roundoff) return invalid(field, 'out_of_range');
      return seconds;
    }
    return value;
  };
  const range = (minText: string | undefined, maxText: string | undefined, scale: number, field: string): TargetRange | undefined => {
    const min = target(minText ?? '', scale, `${field}.min`);
    const max = target(maxText ?? '', scale, `${field}.max`);
    if (min === undefined && max === undefined) return undefined;
    if (min === undefined) return invalid(`${field}.min`, 'required');
    if (max === undefined) return invalid(`${field}.max`, 'required');
    if (min > max) return invalid(field, 'range_reversed');
    return { min, max };
  };
  const distance = form.distanceMode === 'range' ? undefined : target(form.distance, 1000, 'targetDistanceMeters');
  const duration = form.durationMode === 'range' ? undefined : target(form.duration, 60, 'targetDurationSeconds');
  const distanceRange = form.distanceMode === 'range' ? range(form.distanceMin, form.distanceMax, 1000, 'targetDistanceRangeMeters') : undefined;
  const durationRange = form.durationMode === 'range' ? range(form.durationMin, form.durationMax, 60, 'targetDurationRangeSeconds') : undefined;
  if (form.shape === 'loop' && distance === undefined && duration === undefined && !distanceRange && !durationRange) return invalid('targetDistanceMeters', 'target_required');
  return {
    start, ...(destination ? { destination } : {}),
    shape: form.shape as Intent['shape'], profile: form.profile as Intent['profile'], elevation: form.elevation as Intent['elevation'],
    ...(distance === undefined ? {} : { targetDistanceMeters: distance }),
    ...(duration === undefined ? {} : { targetDurationSeconds: duration }),
    ...(distanceRange ? { targetDistanceRangeMeters: distanceRange } : {}),
    ...(durationRange ? { targetDurationRangeSeconds: durationRange } : {}),
  };
}

export function limitations(intent: Draft): string[] {
  const codes: string[] = [];
  if (intent.profile === 'gravel') codes.push('gravel_not_supported');
  if (intent.shape === 'pointToPoint' && intent.elevation && intent.elevation !== 'balanced') codes.push('point_to_point_elevation_not_supported');
  const duration = intent.targetDurationSeconds ?? midpoint(intent.targetDurationRangeSeconds);
  const length = intent.targetDistanceMeters ?? midpoint(intent.targetDistanceRangeMeters) ?? (duration == null ? undefined : duration * 20000 / 3600);
  if (intent.shape === 'loop' && length !== undefined && (length < 1000 || length > 100000)) codes.push('loop_search_distance_out_of_range');
  return codes;
}

function midpoint(range: TargetRange | null | undefined): number | undefined {
  return range ? range.min + (range.max - range.min) / 2 : undefined;
}
