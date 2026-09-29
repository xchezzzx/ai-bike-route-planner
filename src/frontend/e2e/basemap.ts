import type { StyleSpecification } from 'maplibre-gl';

export function cyclingBasemap(subclass: 'cycleway' | 'footway'): StyleSpecification {
  const ids = ['road_path_pedestrian', 'bridge_path_pedestrian', 'tunnel_path_pedestrian'];
  return {
    version: 8,
    sources: Object.fromEntries(ids.map((id, index) => [id, { type: 'geojson', data: {
      type: 'Feature', properties: { class: 'path', subclass, bicycle: 'yes' },
      geometry: { type: 'LineString', coordinates: [[34.786, 32.083 + index * 0.002], [34.794, 32.083 + index * 0.002]] },
    } }])),
    layers: [
      { id: 'land', type: 'background', paint: { 'background-color': '#e8edeb' } },
      ...ids.map(id => ({ id, type: 'line' as const, source: id, minzoom: 14,
        paint: { 'line-color': '#ffffff', 'line-width': 3, 'line-dasharray': [1, 0.7] },
      })),
    ],
  };
}

// Deterministic test-only basemap. Never imported by the application or deployed.
export const basemap: StyleSpecification = {
  version: 8,
  sources: {
    water: { type: 'geojson', attribution: 'Offline test basemap | <a href="https://openfreemap.org/">OpenFreeMap</a>', data: { type: 'Feature', properties: {}, geometry: { type: 'Polygon', coordinates: [[[34.5, 31.8], [34.77, 31.8], [34.77, 32.4], [34.5, 32.4], [34.5, 31.8]]] } } },
    roads: { type: 'geojson', data: { type: 'FeatureCollection', features: [
      { type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: [[34.78, 31.8], [34.78, 32.4]] } },
      { type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: [[34.8, 31.8], [34.8, 32.4]] } },
      { type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: [[34.82, 31.8], [34.82, 32.4]] } },
      { type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: [[34.76, 32.06], [34.95, 32.06]] } },
      { type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: [[34.76, 32.08], [34.95, 32.08]] } },
      { type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: [[34.76, 32.1], [34.95, 32.1]] } },
    ] } },
  },
  layers: [
    { id: 'land', type: 'background', paint: { 'background-color': '#e8edeb' } },
    { id: 'sea', type: 'fill', source: 'water', paint: { 'fill-color': '#a8ccdb' } },
    { id: 'streets', type: 'line', source: 'roads', paint: { 'line-color': '#ffffff', 'line-width': 5 } },
  ],
};
