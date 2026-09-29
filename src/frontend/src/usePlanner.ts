import { useEffect, useRef, useState } from 'react';
import { ApiError, request } from './api';
import { codeText } from './i18n';
import { buildManual, limitations, readCoordinate } from './request';
import type { Candidate, Candidates, GeneratedRoute, Inputs, Intent, Interpretation, RoutePlan } from './types';
import { validPlan } from './routePlan';
import { validRoadCandidates } from './routeQuality';

const initial: Inputs = { locale: 'en', mode: 'prompt', prompt: '', start: { latitude: '', longitude: '' }, destination: { latitude: '', longitude: '' }, manual: { shape: 'loop', profile: 'road', elevation: 'balanced', distance: '', duration: '' } };
type Pending = 'interpreting' | 'validating' | 'generating' | null;
export function usePlanner() {
  const [inputs, setInputs] = useState<Inputs>(initial);
  const [interpretation, setInterpretation] = useState<Interpretation | null>(null);
  const [intent, setIntent] = useState<Intent | null>(null);
  const [results, setResults] = useState<Candidates | null>(null);
  const [refine, setRefineValue] = useState(false);
  const [planning, setPlanning] = useState<RoutePlan | null>(null);
  const [selected, setSelected] = useState(0);
  const [pending, setPending] = useState<Pending>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const revision = useRef(0);
  const active = useRef<AbortController | null>(null);

  function invalidate() {
    revision.current++;
    active.current?.abort();
    active.current = null;
    setPending(null); setError(null); setIntent(null); setInterpretation(null); setResults(null); setPlanning(null); setSelected(0);
  }
  function setRefine(value: boolean) {
    revision.current++;
    active.current?.abort(); active.current = null;
    setPending(null); setError(null); setResults(null); setPlanning(null); setSelected(0); setRefineValue(value);
  }
  function update(patch: Partial<Inputs>) { invalidate(); setInputs(current => ({ ...current, ...patch })); }
  useEffect(() => () => { revision.current++; active.current?.abort(); }, []);

  async function run(kind: NonNullable<Pending>, operation: (signal: AbortSignal) => Promise<() => void>) {
    if (active.current) return;
    const version = ++revision.current;
    const controller = new AbortController();
    active.current = controller;
    setPending(kind); setError(null); setResults(null); setPlanning(null); setSelected(0);
    if (kind !== 'generating') { setIntent(null); setInterpretation(null); }
    try {
      const commit = await operation(controller.signal);
      // Revision fencing is independent of fetch cancellation and covers already-resolved bodies.
      if (version === revision.current && !controller.signal.aborted) commit();
    } catch (cause) {
      if (version === revision.current) setError(cause instanceof ApiError ? cause : new ApiError('invalid_response'));
    } finally {
      if (version === revision.current) { active.current = null; setPending(null); }
    }
  }
  function prepare() {
    return run(inputs.mode === 'prompt' ? 'interpreting' : 'validating', async signal => {
      const start = readCoordinate(inputs.start, 'start');
      const destination = inputs.manual.shape === 'pointToPoint' ? readCoordinate(inputs.destination, 'destination') : undefined;
      if (inputs.mode === 'prompt') {
        if (!inputs.prompt.trim() || inputs.prompt.length > 4000) throw new ApiError('validation_failed', { prompt: [inputs.prompt.trim() ? 'too_long' : 'required'] });
        const response = await request<Interpretation>('/api/route-intents/interpret', { prompt: inputs.prompt, locale: inputs.locale, ...(start ? { start } : {}), ...(destination ? { destination } : {}) }, signal);
        if (!response.draft || !Array.isArray(response.clarifications) || !Array.isArray(response.limitations) || !Array.isArray(response.assumptions) || !['ready', 'unsupported', 'needsClarification'].includes(response.status)) throw new ApiError('invalid_response');
        if (response.intent && !validIntent(response.intent)) throw new ApiError('invalid_response');
        const interpretedShape = response.intent?.shape ?? response.draft.shape;
        if (interpretedShape && interpretedShape !== inputs.manual.shape) {
          return () => {
            setInterpretation({ ...response, status: 'needsClarification', intent: null, clarifications: [
              ...response.clarifications,
              { field: 'shape', code: 'route_shape_conflict', message: codeText(inputs.locale, 'route_shape_conflict') },
            ] });
            setIntent(null);
          };
        }
        const ready = response.status === 'ready' && response.intent && !response.limitations.length && !response.clarifications.length && !limitations(response.intent).length;
        return () => { setInterpretation(response); setIntent(ready ? response.intent : null); };
      }
      const body = buildManual(inputs.manual, start, destination);
      const canonical = await request<Intent>('/api/route-intents/validate', body, signal);
      if (!validIntent(canonical) || canonical.shape !== inputs.manual.shape) throw new ApiError('invalid_response');
      const blocked = limitations(canonical);
      return () => {
        setInterpretation({ status: blocked.length ? 'unsupported' : 'ready', draft: canonical, intent: canonical, assumptions: [], clarifications: [], limitations: blocked });
        setIntent(blocked.length ? null : canonical);
      };
    });
  }
  function generate() {
    if (!intent) return;
    return run('generating', async signal => {
      let response: Candidates;
      let plan: RoutePlan | null = null;
      if (intent.shape === 'loop' && intent.profile === 'road' && refine) {
        plan = await request<RoutePlan>('/api/routes/plan', intent, signal, 100000);
        if (!validPlan(plan)) throw new ApiError('invalid_response');
        response = plan.search;
      } else if (intent.shape === 'loop') response = await request<Candidates>('/api/routes/candidates', intent, signal);
      else {
        const route = await request<GeneratedRoute>('/api/routes/generate', intent, signal);
        response = { requestedLengthMeters: 0, attemptedCount: 1, assumptions: [], warnings: [], candidates: [{ seed: 0, assessment: null, route }], excludedCandidates: [] };
      }
      if (intent.shape === 'loop' ? !validRoadCandidates(response) : !Array.isArray(response.candidates) || !response.candidates.length || !response.candidates.every(validRoute) || !Array.isArray(response.warnings) || !Array.isArray(response.assumptions)) throw new ApiError('invalid_response');
      return () => { setResults(response); setPlanning(plan); };
    });
  }
  function cancel() { invalidate(); setError(new ApiError('cancelled')); }
  return { inputs, update, interpretation, intent, results, selected, select: setSelected, pending, error, prepare, generate, cancel, refine, setRefine, planning };
}

function validIntent(intent: Intent): boolean {
  const coordinate = (point: Intent['start'] | null | undefined) => !!point && Number.isFinite(point.latitude) && Math.abs(point.latitude) <= 90 && Number.isFinite(point.longitude) && Math.abs(point.longitude) <= 180;
  return coordinate(intent.start) && ['loop', 'pointToPoint'].includes(intent.shape)
    && ['road', 'gravel'].includes(intent.profile) && ['balanced', 'minimize', 'seekClimbs'].includes(intent.elevation)
    && (intent.shape === 'loop' ? intent.destination == null : coordinate(intent.destination))
    && (intent.targetDistanceMeters == null || Number.isFinite(intent.targetDistanceMeters) && intent.targetDistanceMeters > 0)
    && (intent.targetDurationSeconds == null || Number.isSafeInteger(intent.targetDurationSeconds) && intent.targetDurationSeconds > 0)
    && (intent.shape === 'pointToPoint' || intent.targetDistanceMeters != null || intent.targetDurationSeconds != null);
}

function validRoute(candidate: Candidate): boolean {
  const route = candidate?.route;
  return !!route && Array.isArray(route.geometry) && route.geometry.length >= 2
    && route.geometry.every(point => Number.isFinite(point.latitude) && Math.abs(point.latitude) <= 90 && Number.isFinite(point.longitude) && Math.abs(point.longitude) <= 180)
    && Number.isFinite(route.distanceMeters) && route.distanceMeters > 0
    && Number.isFinite(route.estimatedDurationSeconds) && route.estimatedDurationSeconds > 0
    && (route.ascentMeters === null || Number.isFinite(route.ascentMeters))
    && (route.descentMeters === null || Number.isFinite(route.descentMeters))
    && typeof route.gpx === 'string' && route.gpx.length > 0 && typeof route.attribution === 'string' && Array.isArray(route.warnings);
}
