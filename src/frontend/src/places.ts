import type { Coordinate } from './types';
import catalog from './data/places.json';
export interface Place extends Coordinate { id: number; name: string; aliases: string[] }
function normalize(value: string) {
  return value.normalize('NFKD').replace(/\p{M}/gu, '').toLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim().replace(/\s+/g, ' ');
}
const places = (catalog as Place[]).map(place => ({ place, names: [...new Set([place.name, ...place.aliases].map(normalize))] }));
export function searchPlaces(query: string): Place[] {
  const q = normalize(query);
  if (q.length < 2 || q.length > 200) return [];
  const matches = places.flatMap(({ place, names }) => {
    const scores = names.filter(name => name.includes(q)).map(name => name === q ? 0 : name.startsWith(q) ? 1 : 2);
    return scores.length ? [{ place, score: Math.min(...scores) }] : [];
  });
  return matches.sort((a, b) => a.score - b.score || a.place.name.localeCompare(b.place.name, 'en') || a.place.id - b.place.id).slice(0, 8).map(x => x.place);
}
export function nearestPlace(point: Coordinate): Place | undefined {
  if (!Number.isFinite(point.latitude) || !Number.isFinite(point.longitude) || Math.abs(point.latitude) > 90 || Math.abs(point.longitude) > 180) return undefined;
  let nearest: Place | undefined; let distance = 10000;
  const radians = Math.PI / 180;
  for (const { place } of places) {
    const a = Math.sin((place.latitude - point.latitude) * radians / 2) ** 2
      + Math.cos(point.latitude * radians) * Math.cos(place.latitude * radians) * Math.sin((place.longitude - point.longitude) * radians / 2) ** 2;
    const meters = 6371000 * 2 * Math.asin(Math.sqrt(Math.min(1, a)));
    if (meters < distance) { distance = meters; nearest = place; }
  }
  return nearest;
}
