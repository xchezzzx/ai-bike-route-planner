import { expect, it } from 'vitest';
import { nearestPlace, searchPlaces } from '../src/places';
import catalog from '../src/data/places.json';
import manifest from '../src/data/places-manifest.json';
import naming from '../../backend/CyclingRoutes.Infrastructure/Naming/Data/settlements.json';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';

it('ships the pinned settlement positions with source aliases and reproducible bytes', () => {
  expect(catalog).toHaveLength(manifest.settlementCount);
  expect(new Set(catalog.map(p => p.id)).size).toBe(catalog.length);
  expect(catalog.map(({ id, name, latitude, longitude }) => ({ id, asciiName: name, latitude, longitude })))
    .toEqual(naming.settlements.map(({ id, asciiName, latitude, longitude }) => ({ id, asciiName, latitude, longitude })));
  const bytes = readFileSync('src/data/places.json');
  expect(createHash('sha256').update(bytes).digest('hex')).toBe(manifest.outputSha256);
  expect(catalog.every(p => p.aliases.every(alias => alias.length <= 200 && !/[\x00-\x1f\x7f]/.test(alias)))).toBe(true);
});

it.each(['Haifa', 'חיפה', 'Хайфа'])('finds Haifa by source aliases: %s', query => {
  expect(searchPlaces(query)[0]?.name).toBe('Haifa');
});
it('normalizes spacing, accents and hyphens without inventing locations', () => {
  expect(searchPlaces('  tel-aviv  ')[0]?.name).toContain('Tel Aviv');
  expect(searchPlaces('unknown settlement xyz')).toEqual([]);
  expect(searchPlaces('')).toEqual([]);
  expect(searchPlaces('a')).toEqual([]);
  expect(searchPlaces('ha').length).toBeLessThanOrEqual(8);
});
it('labels nearby coordinates without changing them and returns no distant place', () => {
  const point = { latitude: 32.814107, longitude: 34.995308 };
  const copy = { ...point };
  expect(nearestPlace(point)?.name).toBe('Haifa');
  expect(point).toEqual(copy);
  expect(nearestPlace({ latitude: 0, longitude: 0 })).toBeUndefined();
});
