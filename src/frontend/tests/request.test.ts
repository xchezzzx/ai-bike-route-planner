import { describe, expect, it } from 'vitest';
import { buildManual, readCoordinate, limitations } from '../src/request';

const form = { shape: 'loop', profile: 'road', elevation: 'balanced', distance: '25.5', duration: '90.5' };
const start = { latitude: 32.08, longitude: 34.78 };

describe('request boundary', () => {
  it('converts km and minutes exactly once and omits an empty destination', () => {
    expect(buildManual(form, start)).toEqual({ start, shape: 'loop', profile: 'road', elevation: 'balanced', targetDistanceMeters: 25500, targetDurationSeconds: 5430 });
  });
  it('omits a blank target instead of sending zero', () => {
    expect(buildManual({ ...form, distance: ' ' }, start)).toEqual({ start, shape: 'loop', profile: 'road', elevation: 'balanced', targetDurationSeconds: 5430 });
    expect(buildManual({ ...form, duration: '' }, start)).not.toHaveProperty('targetDurationSeconds');
  });
  it.each(['0', '-1', 'Infinity', 'NaN', '1e999', 'hello'])('rejects invalid distance %s', distance => {
    expect(() => buildManual({ ...form, distance }, start)).toThrow();
  });
  it.each(['0', '-1', 'Infinity', '0.001', '0.01', '0.025', '65.60001', '1e15'])('rejects invalid duration %s', duration => {
    expect(() => buildManual({ ...form, duration }, start)).toThrow();
  });
  it.each([['4.1', 246], ['8.2', 492], ['16.4', 984], ['32.8', 1968], ['65.6', 3936]])('converts %s minutes despite binary roundoff', (duration, seconds) => {
    expect(buildManual({ ...form, duration: String(duration) }, start).targetDurationSeconds).toBe(seconds);
  });
  it('requires a target and start; A-B needs a different destination; loop must not hide one', () => {
    expect(() => buildManual({ ...form, distance: '', duration: '' }, start)).toThrow();
    expect(() => buildManual(form, undefined)).toThrow();
    expect(() => buildManual({ ...form, shape: 'pointToPoint' }, start)).toThrow();
    expect(() => buildManual({ ...form, shape: 'pointToPoint' }, start, start)).toThrow();
    expect(() => buildManual(form, start, { latitude: 32.1, longitude: 34.8 })).toThrow();
  });
  it('keeps valid zero coordinates and omits a fully empty pair', () => {
    expect(readCoordinate({ latitude: '0', longitude: '0' }, 'start')).toEqual({ latitude: 0, longitude: 0 });
    expect(readCoordinate({ latitude: '', longitude: '' }, 'start')).toBeUndefined();
  });
  it.each([{ latitude: '32', longitude: '' }, { latitude: '91', longitude: '34' }, { latitude: 'NaN', longitude: '34' }, { latitude: '', longitude: '0' }])('rejects partial/out-of-range coordinates %j', value => {
    expect(() => readCoordinate(value, 'start')).toThrow();
  });
  it('reports unsupported choices without changing them', () => {
    expect(limitations({ shape: 'loop', profile: 'gravel', elevation: 'balanced' })).toEqual(['gravel_not_supported']);
    expect(limitations({ shape: 'pointToPoint', profile: 'road', elevation: 'seekClimbs' })).toEqual(['point_to_point_elevation_not_supported']);
    expect(limitations({ shape: 'loop', profile: 'road', elevation: 'balanced', targetDistanceMeters: 100001 })).toEqual(['loop_search_distance_out_of_range']);
  });
});
