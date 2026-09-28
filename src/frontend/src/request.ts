import { ApiError } from './api';
import type { Coordinate, CoordinateInput, Draft, Intent, ManualInput } from './types';

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
    if (field === 'targetDurationSeconds' && (!Number.isSafeInteger(value) || value > 922337203685)) return invalid(field, 'out_of_range');
    return value;
  };
  const distance = target(form.distance, 1000, 'targetDistanceMeters');
  const duration = target(form.duration, 60, 'targetDurationSeconds');
  if (distance === undefined && duration === undefined) return invalid('targetDistanceMeters', 'target_required');
  return {
    start, ...(destination ? { destination } : {}),
    shape: form.shape as Intent['shape'], profile: form.profile as Intent['profile'], elevation: form.elevation as Intent['elevation'],
    ...(distance === undefined ? {} : { targetDistanceMeters: distance }),
    ...(duration === undefined ? {} : { targetDurationSeconds: duration }),
  };
}

export function limitations(intent: Draft): string[] {
  const codes: string[] = [];
  if (intent.profile === 'gravel') codes.push('gravel_not_supported');
  if (intent.shape === 'pointToPoint' && intent.elevation && intent.elevation !== 'balanced') codes.push('point_to_point_elevation_not_supported');
  const length = intent.targetDistanceMeters ?? (intent.targetDurationSeconds == null ? undefined : intent.targetDurationSeconds * 20000 / 3600);
  if (intent.shape === 'loop' && length !== undefined && (length < 1000 || length > 100000)) codes.push('loop_search_distance_out_of_range');
  return codes;
}
