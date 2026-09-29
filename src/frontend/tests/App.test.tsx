import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import App from '../src/App';
import { candidates, intent, interpretation, route, refinement } from './fixtures';

// jsdom has no WebGL; the browser suite exercises the actual MapLibre canvas.
vi.mock('../src/RouteMap', () => ({ default: ({ focus }: { focus?: unknown }) => <div data-testid="map" data-focus={focus ? 'queued' : 'none'} /> }));

let posts: { path: string; body: unknown; signal: AbortSignal }[];
let replies: Record<string, unknown>;
let delay: Promise<Response> | undefined;
beforeEach(() => {
  posts = [];
  delay = undefined;
  replies = { interpret: interpretation, validate: intent, candidates, generate: route, plan: refinement };
  vi.stubGlobal('fetch', vi.fn((path: string, options: RequestInit) => {
    if (path === '/health') return Promise.resolve(new Response('Healthy'));
    posts.push({ path, body: JSON.parse(String(options.body)), signal: options.signal as AbortSignal });
    return delay ?? Promise.resolve(new Response(JSON.stringify(replies[path.split('/').at(-1)!])));
  }));
});

async function setupPrompt() {
  const user = userEvent.setup();
  render(<App />);
  await user.type(screen.getByLabelText('Start latitude'), '32.08');
  await user.type(screen.getByLabelText('Start longitude'), '34.78');
  await user.type(screen.getByLabelText('Ride request'), 'A 25 km road loop');
  return user;
}

it('applies current location only to start and invalidates completed routes', async () => {
  let success!: PositionCallback;
  const getCurrentPosition = vi.fn(callback => { success = callback; });
  vi.stubGlobal('isSecureContext', true);
  Object.defineProperty(navigator, 'geolocation', { value: { getCurrentPosition }, configurable: true });
  const user = await setupPrompt();
  await user.click(screen.getByRole('radio', { name: 'A to B' }));
  await user.type(screen.getByLabelText('Destination latitude'), '32.1');
  await user.type(screen.getByLabelText('Destination longitude'), '34.8');
  await user.click(screen.getByRole('radio', { name: 'Destination' }));
  const ab = { ...intent, shape: 'pointToPoint', destination: { latitude: 32.1, longitude: 34.8 } };
  replies.interpret = { ...interpretation, draft: ab, intent: ab };
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  await screen.findByRole('button', { name: 'Download GPX' });
  expect(getCurrentPosition).not.toHaveBeenCalled();
  await user.click(screen.getByRole('button', { name: 'Use my location as start' }));
  act(() => success({ coords: { latitude: 32.8, longitude: 35, accuracy: 15 } } as GeolocationPosition));
  expect(screen.getByLabelText('Start latitude')).toHaveValue('32.800000');
  expect(screen.getByLabelText('Destination latitude')).toHaveValue('32.1');
  expect(screen.getByRole('button', { name: 'Generate routes' })).toBeDisabled();
  expect(screen.queryByRole('button', { name: 'Download GPX' })).not.toBeInTheDocument();
  expect(screen.getByText(/Start updated/)).toHaveTextContent('Reported accuracy: 15 m');
  expect(posts).toHaveLength(2);
});

it('preserves results on geolocation denial and ignores a fix after manual edits', async () => {
  let success!: PositionCallback; let failure!: PositionErrorCallback;
  vi.stubGlobal('isSecureContext', true);
  Object.defineProperty(navigator, 'geolocation', { value: { getCurrentPosition: vi.fn((ok, error) => { success = ok; failure = error; }) }, configurable: true });
  const user = await setupPrompt();
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  await screen.findByRole('button', { name: 'Download GPX' });
  await user.click(screen.getByRole('button', { name: 'Use my location as start' }));
  act(() => failure({ code: 1 } as GeolocationPositionError));
  expect(screen.getByRole('alert')).toHaveTextContent('Location permission was denied.');
  expect(screen.getByRole('button', { name: 'Download GPX' })).toBeEnabled();
  await user.click(screen.getByRole('button', { name: 'Use my location as start' }));
  await user.clear(screen.getByLabelText('Start latitude'));
  await user.type(screen.getByLabelText('Start latitude'), '32.9');
  act(() => success({ coords: { latitude: 32.8, longitude: 35, accuracy: 15 } } as GeolocationPosition));
  expect(screen.getByLabelText('Start latitude')).toHaveValue('32.9');
});

it('clears deferred location centering when a newer planning action starts', async () => {
  let success!: PositionCallback;
  vi.stubGlobal('isSecureContext', true);
  Object.defineProperty(navigator, 'geolocation', { value: { getCurrentPosition: vi.fn(ok => { success = ok; }) }, configurable: true });
  const user = await setupPrompt();
  await user.click(screen.getByRole('button', { name: 'Use my location as start' }));
  act(() => success({ coords: { latitude: 32.8, longitude: 35, accuracy: 15 } } as GeolocationPosition));
  expect(screen.getByTestId('map')).toHaveAttribute('data-focus', 'queued');
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  expect(screen.getByTestId('map')).toHaveAttribute('data-focus', 'none');
});

it('prepares and generates a manual interval without collapsing it to a scalar', async () => {
  const user = await setupPrompt();
  const ranged = { ...intent, targetDistanceMeters: null, targetDistanceRangeMeters: { min: 35000, max: 45000 } };
  replies.validate = ranged;
  await user.click(screen.getByRole('radio', { name: 'Manual' }));
  const distance = screen.getByRole('group', { name: 'Distance (km)' });
  await user.click(within(distance).getByRole('radio', { name: 'Range' }));
  await user.type(within(distance).getByLabelText('Minimum'), '35');
  await user.type(within(distance).getByLabelText('Maximum'), '45');
  await user.click(screen.getByRole('button', { name: 'Validate preferences' }));
  await screen.findByText('Ready to generate');
  expect(posts[0].body).toMatchObject({ targetDistanceRangeMeters: { min: 35000, max: 45000 } });
  expect(posts[0].body).not.toHaveProperty('targetDistanceMeters');
  expect(screen.getByText('35 km – 45 km')).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  await screen.findByRole('button', { name: 'Download GPX' });
  expect(posts[1].body).toEqual(ranged);
  await user.click(within(distance).getByRole('radio', { name: 'Target' }));
  expect(screen.getByRole('button', { name: 'Generate routes' })).toBeDisabled();
  expect(screen.queryByRole('button', { name: 'Download GPX' })).not.toBeInTheDocument();
});

it.each([
  { targetDistanceRangeMeters: { min: 40000, max: 30000 } },
  { targetDistanceMeters: 25000, targetDistanceRangeMeters: { min: 20000, max: 30000 } },
  { targetDistanceRangeMeters: { min: 20000 } },
  { targetDurationRangeSeconds: { min: 60.5, max: 90 } },
])('rejects an invalid canonical interval %j', async patch => {
  const user = await setupPrompt();
  const bad = { ...intent, targetDistanceMeters: null, ...patch };
  replies.interpret = { ...interpretation, draft: bad, intent: bad };
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('The API returned an unusable response.');
  expect(screen.getByRole('button', { name: 'Generate routes' })).toBeDisabled();
});

async function prepareRefinement() {
  const user = await setupPrompt();
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  await screen.findByText('Ready to generate');
  const toggle = screen.getByRole('checkbox', { name: 'AI refinement' });
  expect(toggle).not.toBeChecked();
  await user.click(toggle);
  expect(posts).toHaveLength(1);
  expect(screen.getByRole('button', { name: 'Generate routes' })).toBeEnabled();
  return user;
}

it('uses one shape control and restores the inactive destination when returning to A-B', async () => {
  const user = await setupPrompt();
  expect(screen.getByRole('radio', { name: 'Loop' })).toBeChecked();
  expect(screen.queryByLabelText('Destination latitude')).not.toBeInTheDocument();
  expect(screen.queryByRole('radio', { name: 'Destination' })).not.toBeInTheDocument();
  await user.click(screen.getByRole('radio', { name: 'A to B' }));
  await user.type(screen.getByLabelText('Destination latitude'), '32.1');
  await user.type(screen.getByLabelText('Destination longitude'), '34.8');
  await user.click(screen.getByRole('radio', { name: 'Destination' }));
  await user.click(screen.getByRole('radio', { name: 'Loop' }));
  expect(screen.queryByLabelText('Destination latitude')).not.toBeInTheDocument();
  await user.click(screen.getByRole('radio', { name: 'Manual' }));
  expect(screen.queryByRole('combobox', { name: 'Route shape' })).not.toBeInTheDocument();
  await user.click(screen.getByRole('radio', { name: 'A to B' }));
  expect(screen.getByLabelText('Destination latitude')).toHaveValue('32.1');
  expect(screen.getByLabelText('Destination longitude')).toHaveValue('34.8');
  expect(screen.getByRole('radio', { name: 'Start' })).toBeChecked();
});

it.each(['Prompt', 'Manual'])('omits even malformed hidden destination in %s mode', async mode => {
  const user = await setupPrompt();
  await user.click(screen.getByRole('radio', { name: 'A to B' }));
  await user.type(screen.getByLabelText('Destination latitude'), 'bad');
  await user.click(screen.getByRole('radio', { name: 'Loop' }));
  if (mode === 'Manual') {
    await user.click(screen.getByRole('radio', { name: 'Manual' }));
    await user.type(screen.getByLabelText('Distance (km)'), '25');
  }
  await user.click(screen.getByRole('button', { name: mode === 'Manual' ? 'Validate preferences' : 'Interpret request' }));
  await screen.findByText('Ready to generate');
  expect(posts).toHaveLength(1);
  expect(posts[0].body).not.toHaveProperty('destination');
});

it.each([false, true])('requires clarification when prompt shape contradicts selection (A-B=%s)', async ab => {
  const user = await setupPrompt();
  if (ab) await user.click(screen.getByRole('radio', { name: 'A to B' }));
  const conflicting = ab ? intent : { ...intent, shape: 'pointToPoint', destination: { latitude: 32.1, longitude: 34.8 } };
  replies.interpret = { ...interpretation, draft: conflicting, intent: conflicting };
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  expect(await screen.findByText('The request describes a different route shape. Change the selected mode or edit the request.')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Generate routes' })).toBeDisabled();
  expect(screen.queryByText('Ready to generate')).not.toBeInTheDocument();
});

it('switching shape clears results and fences late preparation', async () => {
  const user = await setupPrompt();
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  await screen.findByRole('button', { name: 'Download GPX' });
  await user.click(screen.getByRole('radio', { name: 'A to B' }));
  expect(screen.queryByRole('button', { name: 'Download GPX' })).not.toBeInTheDocument();
  let resolve!: (response: Response) => void;
  delay = new Promise(done => { resolve = done; });
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  const pendingRequest = posts.at(-1)!;
  await user.click(screen.getByRole('radio', { name: 'Loop' }));
  expect(pendingRequest.signal.aborted).toBe(true);
  await act(async () => { resolve(new Response(JSON.stringify(interpretation))); });
  expect(screen.getByRole('button', { name: 'Generate routes' })).toBeDisabled();
  expect(screen.queryByText('Ready to generate')).not.toBeInTheDocument();
});

it.each([false, true])('shows explained no-match results after a successful result (AI=%s)', async advised => {
  const user = advised ? await prepareRefinement() : await setupPrompt();
  if (!advised) await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  await screen.findByRole('button', { name: 'Download GPX' });
  const empty = { ...candidates, candidates: [], excludedCandidates: [{ seed: 1, distanceMeters: 40000, estimatedDurationSeconds: 4500,
    assessment: { ...candidates.candidates[0].assessment!, targetsMatched: false }, reasons: ['targets_not_met'] }],
    warnings: ['no_candidate_meets_requirements', 'candidate_generation_incomplete', 'routing_timeout'] };
  if (advised) replies.plan = { ...refinement, search: empty };
  else replies.candidates = empty;
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  await screen.findByText('No routes meet these requirements.');
  expect(screen.queryByRole('button', { name: 'Download GPX' })).not.toBeInTheDocument();
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  await user.click(screen.getByText('Excluded routes'));
  expect(screen.getByText('This route does not meet the target tolerance.')).toBeInTheDocument();
  if (advised) expect(screen.getByText('AI-guided search completed')).toBeInTheDocument();
});

it.each([false, true])('shows surface uncertainty and small known non-road coverage (unknown=%s)', async unknown => {
  const response = structuredClone(candidates);
  const q = response.candidates[0].assessment!.quality;
  q.surfaceEvidenceState = unknown ? 'unavailable' : 'partial';
  q.surface = unknown
    ? { pavedMeters: 0, nonRoadMeters: 0, otherKnownMeters: 0, unknownMeters: q.geometryLengthMeters }
    : { pavedMeters: 4900, nonRoadMeters: 100, otherKnownMeters: 0, unknownMeters: 5000 };
  replies.candidates = response;
  const user = await setupPrompt();
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  const quality = await screen.findByRole('region', { name: 'Road data' });
  expect(within(quality).getByText(unknown ? 'Surface data is unavailable.' : 'Surface data is incomplete.')).toBeInTheDocument();
  expect(within(quality).getByText(unknown ? 'Unknown surface' : 'Unpaved / loose surface').parentElement).toHaveTextContent(unknown ? '10 km' : '0.1 km');
  expect(screen.getByRole('button', { name: 'Download GPX' })).toBeEnabled();
});

it('opts into refinement only on Generate and renders application-owned trace', async () => {
  const user = await prepareRefinement();
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  await screen.findByRole('region', { name: 'Routes' });
  expect(posts[1]).toMatchObject({ path: '/api/routes/plan', body: intent });
  expect(screen.getByText('AI-guided search completed')).toBeInTheDocument();
  await user.click(screen.getByText('Search details'));
  expect(screen.getByText('Duplicate route')).toBeInTheDocument();
  expect(posts).toHaveLength(2);
});

it.each([
  ['failed', 'quota', 'AI unavailable; ordinary search retained'],
  ['stopped', null, 'Advisor stopped further search'],
])('renders %s status without model prose', async (advisorStatus, advisorFailure, label) => {
  replies.plan = { ...refinement, advisorStatus, advisorFailure, ...(advisorStatus === 'stopped' ? { search: { ...candidates, attemptedCount: 2 }, attempts: refinement.attempts.slice(0, 2) } : {}) };
  const user = await prepareRefinement();
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  expect(await screen.findByText(label!)).toBeInTheDocument();
});

it.each([
  { advisorStatus: 'model free text' }, { advisorFailure: 'private secret' }, { advisorCallCount: 2 },
  { attempts: [{ ...refinement.attempts[0], outcome: 'unknown' }] },
  { attempts: [{ ...refinement.attempts[0], requestedLengthMeters: -1 }, ...refinement.attempts.slice(1)] },
  { attempts: [{ ...refinement.attempts[0], reason: 'safe road' }, ...refinement.attempts.slice(1)] },
  { attempts: [{ ...refinement.attempts[0], failure: 'private secret' }, ...refinement.attempts.slice(1)] },
])('rejects invalid refinement metadata %j', async patch => {
  replies.plan = { ...refinement, ...patch };
  const user = await prepareRefinement();
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('The API returned an unusable response.');
  expect(screen.queryByRole('button', { name: 'Download GPX' })).not.toBeInTheDocument();
});

it('changing refinement cancels and fences a late result while preserving prepared intent', async () => {
  const user = await prepareRefinement();
  let resolve!: (value: Response) => void;
  delay = new Promise(done => { resolve = done; });
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  await user.click(screen.getByRole('checkbox', { name: 'AI refinement' }));
  expect(posts[1].signal.aborted).toBe(true);
  await act(async () => { resolve(new Response(JSON.stringify(refinement))); });
  expect(screen.queryByRole('region', { name: 'Routes' })).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Generate routes' })).toBeEnabled();
});

it.each([false, true])('refinement waits beyond 60 seconds and enforces 100-second deadline (timeout=%s)', async timesOut => {
  await prepareRefinement();
  let resolve!: (value: Response) => void;
  delay = new Promise(done => { resolve = done; });
  vi.useFakeTimers();
  fireEvent.click(screen.getByRole('button', { name: 'Generate routes' }));
  await act(async () => { await vi.advanceTimersByTimeAsync(65000); });
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  expect(posts[1].signal.aborted).toBe(false);
  if (timesOut) {
    await act(async () => { await vi.advanceTimersByTimeAsync(35000); });
    expect(screen.getByRole('alert')).toHaveTextContent('The request timed out. Retry manually.');
    expect(posts[1].signal.aborted).toBe(true);
  } else {
    await act(async () => { resolve(new Response(JSON.stringify(refinement))); });
    expect(screen.getByRole('region', { name: 'Routes' })).toBeInTheDocument();
  }
  vi.useRealTimers();
});

it('ignores previous refinement preference for an A-B intent', async () => {
  const user = await prepareRefinement();
  await user.click(screen.getByRole('radio', { name: 'A to B' }));
  replies.interpret = { ...interpretation, intent: { ...intent, shape: 'pointToPoint', destination: { latitude: 32.1, longitude: 34.8 } } };
  await user.type(screen.getByLabelText('Ride request'), ' to destination');
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  await screen.findByText('Ready to generate');
  expect(screen.queryByRole('checkbox', { name: 'AI refinement' })).not.toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  await screen.findByRole('region', { name: 'Routes' });
  expect(posts.at(-1)?.path).toBe('/api/routes/generate');
});

it('interprets first, requires explicit generation, renders real metrics and downloads selected GPX', async () => {
  const user = await setupPrompt();
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  expect(await screen.findByText('Ready to generate')).toBeInTheDocument();
  expect(posts).toHaveLength(1);
  expect(posts[0].body).toEqual({ prompt: 'A 25 km road loop', locale: 'en', start: { latitude: 32.08, longitude: 34.78 } });
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  const results = await screen.findByRole('region', { name: 'Routes' });
  expect(within(results).getByText('24.5 km')).toBeInTheDocument();
  expect(within(results).getAllByText('Unknown').length).toBeGreaterThan(0);
  expect(posts[1]).toMatchObject({ path: '/api/routes/candidates', body: intent });
  await user.click(screen.getByRole('radio', { name: /Route 2/ }));
  expect(screen.getByText('120 m')).toBeInTheDocument();
  const create = vi.fn((_blob: Blob) => 'blob:selected');
  vi.stubGlobal('URL', { createObjectURL: create, revokeObjectURL: vi.fn() });
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
  await user.click(screen.getByRole('button', { name: 'Download GPX' }));
  expect(create.mock.calls[0][0]).toBeInstanceOf(Blob);
  const blob = create.mock.calls[0][0] as unknown as Blob;
  const content = await new Promise(resolve => { const reader = new FileReader(); reader.onload = () => resolve(reader.result); reader.readAsText(blob); });
  expect(content).toBe(candidates.candidates[1].route.gpx);
  expect(posts).toHaveLength(2);
});

it.each(['unsupported', 'needsClarification'])('never enables generation for %s even if an intent is present', async status => {
  replies.interpret = { ...interpretation, status, limitations: ['gravel_not_supported'], clarifications: [{ field: 'profile', code: 'ambiguous', message: 'Choose a profile.' }] };
  const user = await setupPrompt();
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  expect(await screen.findByText('Gravel routing is not supported.')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Generate routes' })).toBeDisabled();
  expect(screen.getByText('Choose a profile.')).toBeInTheDocument();
});

it('validates manual A-B and sends the backend canonical intent to generation', async () => {
  replies.validate = { ...intent, shape: 'pointToPoint', destination: { latitude: 32.1, longitude: 34.8 }, targetDistanceMeters: 25123 };
  const user = await setupPrompt();
  await user.click(screen.getByRole('radio', { name: 'Manual' }));
  await user.click(screen.getByRole('radio', { name: 'A to B' }));
  await user.type(screen.getByLabelText('Destination latitude'), '32.1');
  await user.type(screen.getByLabelText('Destination longitude'), '34.8');
  await user.type(screen.getByLabelText('Distance (km)'), '25');
  await user.click(screen.getByRole('button', { name: 'Validate preferences' }));
  expect(await screen.findByText('Ready to generate')).toBeInTheDocument();
  expect(posts[0]).toMatchObject({ path: '/api/route-intents/validate', body: { targetDistanceMeters: 25000 } });
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  await screen.findByRole('region', { name: 'Routes' });
  expect(posts[1]).toMatchObject({ path: '/api/routes/generate', body: replies.validate });
});

it('validates and generates manual A-B without distance or duration', async () => {
  replies.validate = { ...intent, shape: 'pointToPoint', destination: { latitude: 32.1, longitude: 34.8 }, targetDistanceMeters: null, targetDurationSeconds: null };
  const user = await setupPrompt();
  await user.click(screen.getByRole('radio', { name: 'Manual' }));
  await user.click(screen.getByRole('radio', { name: 'A to B' }));
  await user.type(screen.getByLabelText('Destination latitude'), '32.1');
  await user.type(screen.getByLabelText('Destination longitude'), '34.8');
  await user.click(screen.getByRole('button', { name: 'Validate preferences' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Generate routes' })).toBeEnabled());
  expect(posts[0].body).not.toHaveProperty('targetDistanceMeters');
  expect(posts[0].body).not.toHaveProperty('targetDurationSeconds');
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  await screen.findByRole('region', { name: 'Routes' });
  expect(posts[1]).toMatchObject({ path: '/api/routes/generate', body: replies.validate });
});

it('blocks unsupported canonical manual preferences after validation', async () => {
  replies.validate = { ...intent, profile: 'gravel' };
  const user = await setupPrompt();
  await user.click(screen.getByRole('radio', { name: 'Manual' }));
  await user.selectOptions(screen.getByLabelText('Cycling profile'), 'gravel');
  await user.type(screen.getByLabelText('Distance (km)'), '25');
  await user.click(screen.getByRole('button', { name: 'Validate preferences' }));
  expect(await screen.findByText('Gravel routing is not supported.')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Generate routes' })).toBeDisabled();
  expect(posts).toHaveLength(1);
});

it('disables unsupported manual options without discarding a previously selected loop preference', async () => {
  const user = await setupPrompt();
  await user.click(screen.getByRole('radio', { name: 'Manual' }));
  expect(screen.getByRole('option', { name: 'Gravel' })).toBeDisabled();
  await user.selectOptions(screen.getByLabelText('Elevation preference'), 'minimize');
  await user.click(screen.getByRole('radio', { name: 'A to B' }));
  expect(screen.getByRole('option', { name: 'Minimize climbs' })).toBeDisabled();
  expect(screen.getByRole('option', { name: 'Seek climbs' })).toBeDisabled();
  expect(screen.getByLabelText('Elevation preference')).toHaveValue('minimize');
  await user.selectOptions(screen.getByLabelText('Elevation preference'), 'balanced');
  expect(screen.getByLabelText('Elevation preference')).toHaveValue('balanced');
});

it('connects the disabled Generate button to its current preparation state', async () => {
  const user = await setupPrompt();
  const generate = screen.getByRole('button', { name: 'Generate routes' });
  expect(generate).toHaveAccessibleDescription('Request not validated');
  replies.interpret = { ...interpretation, status: 'needsClarification', intent: null, clarifications: [{ field: 'profile', code: 'required', message: 'Choose a profile.' }] };
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  await waitFor(() => expect(generate).toHaveAccessibleDescription('Clarification needed'));
  expect(generate).toBeDisabled();
  await user.selectOptions(screen.getByLabelText('Language'), 'ru');
  expect(screen.getByRole('button', { name: 'Построить маршруты' })).toHaveAccessibleDescription('Запрос не проверен');
});

it.each(['prompt', 'mode', 'locale', 'coordinate', 'cancel'])('aborts pending interpretation and discards late responses after %s changes', async change => {
  let resolve!: (value: Response) => void;
  delay = new Promise(done => { resolve = done; });
  const user = await setupPrompt();
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  await waitFor(() => expect(posts).toHaveLength(1));
  if (change === 'prompt') await user.type(screen.getByLabelText('Ride request'), '!');
  if (change === 'mode') await user.click(screen.getByRole('radio', { name: 'Manual' }));
  if (change === 'locale') await user.selectOptions(screen.getByLabelText('Language'), 'ru');
  if (change === 'coordinate') await user.type(screen.getByLabelText('Start latitude'), '1');
  if (change === 'cancel') await user.click(screen.getByRole('button', { name: 'Cancel' }));
  expect(posts[0].signal.aborted).toBe(true);
  await act(async () => { resolve(new Response(JSON.stringify(interpretation))); });
  expect(screen.queryByText('Ready to generate')).not.toBeInTheDocument();
  expect(screen.queryByRole('region', { name: 'Routes' })).not.toBeInTheDocument();
});

it('clears completed results when a coordinate changes', async () => {
  const user = await setupPrompt();
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  await screen.findByText('Ready to generate');
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  await screen.findByRole('region', { name: 'Routes' });
  await user.clear(screen.getByLabelText('Start latitude'));
  expect(screen.queryByRole('region', { name: 'Routes' })).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Generate routes' })).toBeDisabled();
});

it('aborts a pending operation when the screen unmounts', async () => {
  let resolve!: (value: Response) => void;
  delay = new Promise(done => { resolve = done; });
  const user = await setupPrompt();
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  cleanup();
  expect(posts[0].signal.aborted).toBe(true);
  await act(async () => { resolve(new Response(JSON.stringify(interpretation))); });
  expect(screen.queryByText('Ready to generate')).not.toBeInTheDocument();
});

it('clears manual validation when preferences change', async () => {
  const user = await setupPrompt();
  await user.click(screen.getByRole('radio', { name: 'Manual' }));
  await user.type(screen.getByLabelText('Distance (km)'), '25');
  await user.click(screen.getByRole('button', { name: 'Validate preferences' }));
  await screen.findByText('Ready to generate');
  await user.selectOptions(screen.getByLabelText('Elevation preference'), 'minimize');
  expect(screen.queryByText('Ready to generate')).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Generate routes' })).toBeDisabled();
});

it('rejects malformed canonical intent rather than enabling routing', async () => {
  replies.interpret = { ...interpretation, intent: { ...intent, shape: 'unexpected' } };
  const user = await setupPrompt();
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('The API returned an unusable response.');
  expect(screen.getByRole('button', { name: 'Generate routes' })).toBeDisabled();
});

it('rejects malformed generated routes rather than exposing an unusable download', async () => {
  replies.candidates = { ...candidates, candidates: [{ ...candidates.candidates[0], route: { ...route, geometry: [{ latitude: 1000, longitude: 34.78 }, { latitude: 32, longitude: 34.8 }] } }] };
  const user = await setupPrompt();
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  await screen.findByText('Ready to generate');
  await user.click(screen.getByRole('button', { name: 'Generate routes' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('The API returned an unusable response.');
  expect(screen.queryByRole('button', { name: 'Download GPX' })).not.toBeInTheDocument();
});

it('localizes Hebrew, preserves numeric LTR and clears prior interpretation on locale change', async () => {
  const user = await setupPrompt();
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  await screen.findByText('Ready to generate');
  await user.selectOptions(screen.getByLabelText('Language'), 'he');
  expect(document.documentElement).toHaveAttribute('lang', 'he');
  expect(document.documentElement).toHaveAttribute('dir', 'rtl');
  expect(screen.getByRole('button', { name: 'יצירת מסלולים' })).toBeDisabled();
  expect(screen.getByLabelText('קו רוחב של התחלה')).toHaveAttribute('dir', 'ltr');
});

it('renders safe localized validation messages, not unknown backend text', async () => {
  const user = await setupPrompt();
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ detail: 'secret', errors: { 'start.latitude': ['out_of_range'], unknown: ['<secret>'] } }), { status: 400 })));
  await user.click(screen.getByRole('button', { name: 'Interpret request' }));
  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent('Use a value within the supported range.');
  expect(alert).not.toHaveTextContent('secret');
});

it('rejects partial coordinates locally without sending a provider request', async () => {
  render(<App />);
  fireEvent.change(screen.getByLabelText('Ride request'), { target: { value: 'A road loop' } });
  fireEvent.change(screen.getByLabelText('Start latitude'), { target: { value: '32' } });
  fireEvent.click(screen.getByRole('button', { name: 'Interpret request' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Please specify a value.');
  expect(posts).toHaveLength(0);
});
