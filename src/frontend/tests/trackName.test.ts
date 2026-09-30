import { expect, it } from 'vitest';
import { trackName } from '../src/trackName';

it('uses a canonical server name without rebuilding it from requested distance', () => {
  expect(trackName({ name: 'Tel-Aviv-Haifa-road-105' }, 0)).toBe('Tel-Aviv-Haifa-road-105');
});
it.each([undefined, null, '', '../route', 'C:\\route', 'a/b', 'a.gpx', 'x\n', 'a'.repeat(121), '<script>', 42])('falls back for a legacy or unsafe name %j', name => {
  expect(trackName({ name } as { name?: string | null }, 1)).toBe('cycling-route-2');
});
