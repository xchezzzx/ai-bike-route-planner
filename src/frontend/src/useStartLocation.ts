import { useEffect, useRef, useState } from 'react';
import type { Coordinate } from './types';

type Failure = 'locationDenied' | 'locationUnavailable' | 'locationTimeout' | 'locationUnsupported' | 'locationInsecure';
type State = { status: 'idle' | 'locating' } | { status: 'error'; code: Failure }
  | { status: 'confirm' | 'applied'; point: Coordinate; accuracy: number };
type Request = { revision: number; point?: Coordinate; accuracy?: number };

export function useStartLocation(getRevision: () => number, onApply: (point: Coordinate, accuracy: number) => void) {
  const [state, setState] = useState<State>({ status: 'idle' });
  const active = useRef<Request | undefined>(undefined);
  const latest = useRef({ getRevision, onApply });
  latest.current = { getRevision, onApply };
  useEffect(() => () => { active.current = undefined; }, []);

  function current(request: Request) {
    return active.current === request && latest.current.getRevision() === request.revision;
  }
  function apply(request: Request) {
    if (!current(request) || !request.point || request.accuracy === undefined) return;
    active.current = undefined;
    latest.current.onApply(request.point, request.accuracy);
    setState({ status: 'applied', point: request.point, accuracy: request.accuracy });
  }
  function cancel() { active.current = undefined; setState({ status: 'idle' }); }
  function locate() {
    const request: Request = { revision: latest.current.getRevision() };
    active.current = request;
    const fail = (code: Failure) => {
      if (!current(request)) return;
      active.current = undefined; setState({ status: 'error', code });
    };
    if (globalThis.isSecureContext === false) return fail('locationInsecure');
    if (!navigator.geolocation) return fail('locationUnsupported');
    setState({ status: 'locating' });
    try {
      navigator.geolocation.getCurrentPosition(position => {
        if (!current(request)) return;
        const { latitude, longitude, accuracy } = position.coords;
        if (!Number.isFinite(latitude) || Math.abs(latitude) > 90 || !Number.isFinite(longitude) || Math.abs(longitude) > 180
          || !Number.isFinite(accuracy) || accuracy < 0) return fail('locationUnavailable');
        request.point = { latitude, longitude }; request.accuracy = accuracy;
        if (accuracy > 100) setState({ status: 'confirm', point: request.point, accuracy });
        else apply(request);
      }, error => fail(error.code === 1 ? 'locationDenied' : error.code === 3 ? 'locationTimeout' : 'locationUnavailable'),
      { enableHighAccuracy: true, timeout: 10000, maximumAge: 0 });
    } catch { fail('locationUnavailable'); }
  }
  return { state, locate, cancel, confirm: () => { if (active.current) apply(active.current); } };
}
