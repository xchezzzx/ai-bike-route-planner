import { expect, it, vi } from 'vitest';
import { request } from '../src/api';
import { codeText, t } from '../src/i18n';

it.each([
  [401, 'access_unauthorized'],
  [403, 'access_origin_forbidden'],
  [429, 'access_rate_limited'],
] as const)('preserves protected access error %s without displaying server details', async (status, code) => {
  const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({ code, detail: 'private server detail' }), { status }));
  vi.stubGlobal('fetch', fetcher);
  await expect(request('/api/routes/candidates', {}, new AbortController().signal)).rejects.toMatchObject({ code, message: code });
  expect(fetcher).toHaveBeenCalledTimes(1);
  for (const locale of ['en', 'ru', 'he'] as const) {
    const message = codeText(locale, code, 'server_error');
    expect(message).not.toBe(t(locale, 'server_error'));
    expect(message).not.toContain('private server detail');
    expect(message).not.toBe(code);
  }
});
