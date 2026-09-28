import { expect, it } from 'vitest';
import { backendUrl } from '../backend-url';

it('defaults to the local API', () => { expect(backendUrl()).toBe('http://127.0.0.1:5080'); });
it.each(['http://127.0.0.1:5081', 'http://localhost:5080/', 'http://[::1]:5080'])('allows local HTTP root %s', value => {
  expect(backendUrl(value)).toBe(new URL(value).origin);
});
it.each(['https://localhost:5080', 'http://example.com', 'http://0.0.0.0:5080', 'http://127.0.0.2:5080', 'http://user:pass@localhost:5080', 'http://localhost:5080/api', 'http://localhost:5080?key=secret', 'http://localhost:5080#hash', '', 'not a URL', 'ftp://localhost', 'http://localhost.example.com'])('rejects unsafe backend target %s', value => {
  expect(() => backendUrl(value)).toThrow('BACKEND_URL');
});
