import { act, renderHook } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { useStartLocation } from '../src/useStartLocation';

let callbacks: { success: PositionCallback; error: PositionErrorCallback | null; options?: PositionOptions }[];
let revision: number;
const apply = vi.fn();
const position = (accuracy = 10): GeolocationPosition => ({ coords: { latitude: 32.8, longitude: 35.0, accuracy }, timestamp: Date.now() }) as GeolocationPosition;
beforeEach(() => {
  callbacks = []; revision = 0; apply.mockReset();
  vi.stubGlobal('isSecureContext', true);
  vi.stubGlobal('navigator', { geolocation: { getCurrentPosition: vi.fn((success, error, options) => callbacks.push({ success, error, options })) } });
});
function setup() { return renderHook(() => useStartLocation(() => revision, apply)); }

it('requests only on demand and applies a precise fix with its reported accuracy', () => {
  const { result } = setup();
  expect(callbacks).toHaveLength(0);
  act(() => result.current.locate());
  expect(callbacks[0].options).toEqual({ enableHighAccuracy: true, timeout: 10000, maximumAge: 0 });
  act(() => callbacks[0].success(position(100)));
  expect(apply).toHaveBeenCalledWith({ latitude: 32.8, longitude: 35 }, 100);
  expect(result.current.state).toMatchObject({ status: 'applied', accuracy: 100 });
});
it('requires confirmation for a coarse fix and permits dismissal without changes', () => {
  const { result } = setup();
  act(() => result.current.locate());
  act(() => callbacks[0].success(position(101)));
  expect(apply).not.toHaveBeenCalled();
  expect(result.current.state).toMatchObject({ status: 'confirm', accuracy: 101 });
  act(() => result.current.confirm());
  expect(apply).toHaveBeenCalledTimes(1);
  act(() => result.current.locate());
  act(() => callbacks[1].success(position(1000)));
  act(() => result.current.cancel());
  act(() => result.current.confirm());
  expect(apply).toHaveBeenCalledTimes(1);
});
it.each([1, 2, 3])('reports location error %s without applying a position', code => {
  const { result } = setup();
  act(() => result.current.locate());
  act(() => callbacks[0].error!({ code } as GeolocationPositionError));
  expect(result.current.state).toMatchObject({ status: 'error', code: ['locationDenied', 'locationUnavailable', 'locationTimeout'][code - 1] });
  expect(apply).not.toHaveBeenCalled();
});
it('fences callbacks after edits, newer requests, cancellation and unmount', () => {
  const { result, unmount } = setup();
  act(() => result.current.locate()); revision++;
  act(() => callbacks[0].success(position()));
  expect(apply).not.toHaveBeenCalled();
  act(() => result.current.locate());
  act(() => result.current.locate());
  act(() => callbacks[1].success(position()));
  expect(apply).not.toHaveBeenCalled();
  act(() => result.current.cancel());
  act(() => callbacks[2].success(position()));
  act(() => result.current.locate()); unmount();
  act(() => callbacks[3].success(position()));
  expect(apply).not.toHaveBeenCalled();
});
it('fences a coarse fix confirmation after edits', () => {
  const { result } = setup();
  act(() => result.current.locate());
  act(() => callbacks[0].success(position(500)));
  revision++;
  act(() => result.current.confirm());
  expect(apply).not.toHaveBeenCalled();
});
it.each([NaN, -1, Infinity])('rejects invalid accuracy %s', accuracy => {
  const { result } = setup();
  act(() => result.current.locate());
  act(() => callbacks[0].success(position(accuracy)));
  expect(result.current.state).toMatchObject({ status: 'error', code: 'locationUnavailable' });
  expect(apply).not.toHaveBeenCalled();
});
it('handles unsupported and insecure environments without invoking geolocation', () => {
  vi.stubGlobal('isSecureContext', false);
  const { result } = setup();
  act(() => result.current.locate());
  expect(result.current.state).toMatchObject({ status: 'error', code: 'locationInsecure' });
  vi.stubGlobal('isSecureContext', true);
  vi.stubGlobal('navigator', {});
  act(() => result.current.locate());
  expect(result.current.state).toMatchObject({ status: 'error', code: 'locationUnsupported' });
  expect(callbacks).toHaveLength(0);
});
