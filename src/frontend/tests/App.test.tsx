import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import App from '../src/App';
import { candidates, intent, interpretation, route } from './fixtures';

// jsdom has no WebGL; the browser suite exercises the actual MapLibre canvas.
vi.mock('../src/RouteMap', () => ({ default: () => <div /> }));

let posts: { path: string; body: unknown; signal: AbortSignal }[];
let replies: Record<string, unknown>;
let delay: Promise<Response> | undefined;
beforeEach(() => {
  posts = [];
  delay = undefined;
  replies = { interpret: interpretation, validate: intent, candidates, generate: route };
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
  await user.selectOptions(screen.getByLabelText('Route shape'), 'pointToPoint');
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
  await user.selectOptions(screen.getByLabelText('Route shape'), 'pointToPoint');
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
  await user.selectOptions(screen.getByLabelText('Route shape'), 'pointToPoint');
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
