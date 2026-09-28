import { expect, it, vi } from 'vitest';
import { request, ApiError } from '../src/api';

it('posts JSON using the given signal and returns the contract body', async () => {
  const fetcher = vi.fn().mockResolvedValue(new Response('{"shape":"loop"}'));
  vi.stubGlobal('fetch', fetcher);
  const signal = new AbortController().signal;
  expect(await request('/api/route-intents/validate', { shape: 'loop' }, signal)).toEqual({ shape: 'loop' });
  expect(fetcher).toHaveBeenCalledWith('/api/route-intents/validate', expect.objectContaining({ method: 'POST', body: '{"shape":"loop"}', signal: expect.any(AbortSignal) }));
});

it('keeps known error codes and validation fields but never exposes provider text', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'routing_rate_limited', detail: '<secret>', title: 'secret provider' }), { status: 503 })));
  await expect(request('/api/routes/candidates', {}, new AbortController().signal)).rejects.toMatchObject({ code: 'routing_rate_limited', message: 'routing_rate_limited' });
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ errors: { 'start.latitude': ['out_of_range'] } }), { status: 400 })));
  await expect(request('/api/route-intents/validate', {}, new AbortController().signal)).rejects.toMatchObject({ code: 'validation_failed', fields: { 'start.latitude': ['out_of_range'] } });
});

it('maps HTML failures and invalid success JSON to safe errors', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('<html>private failure</html>', { status: 502 })));
  await expect(request('/api/routes/candidates', {}, new AbortController().signal)).rejects.toMatchObject({ code: 'server_error' });
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('not json')));
  await expect(request('/api/routes/candidates', {}, new AbortController().signal)).rejects.toMatchObject({ code: 'invalid_response' });
});

it('maps network errors without leaking their content', async () => {
  vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('private URL')));
  await expect(request('/api/routes/candidates', {}, new AbortController().signal)).rejects.toMatchObject({ code: 'network_error' });
});

it('cancels a hung transport even if it ignores abort', async () => {
  const controller = new AbortController();
  vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})));
  const promise = request('/api/routes/candidates', {}, controller.signal);
  const assertion = expect(promise).rejects.toMatchObject({ code: 'cancelled' });
  controller.abort();
  await assertion;
});

it('bounds the entire request including stalled response-body reading to 60 seconds', async () => {
  vi.useFakeTimers();
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: () => new Promise(() => {}) }));
  let error: unknown;
  const outcome = request('/api/routes/candidates', {}, new AbortController().signal).catch(cause => { error = cause; });
  await vi.advanceTimersByTimeAsync(59999);
  expect(error).toBeUndefined();
  await vi.advanceTimersByTimeAsync(1);
  expect(error).toBeInstanceOf(ApiError);
  expect(error).toMatchObject({ code: 'request_timeout' });
  await outcome;
  expect(vi.getTimerCount()).toBe(0);
});
