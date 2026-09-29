import type { Map } from 'maplibre-gl';

const pathLayers = new Set(['road_path_pedestrian', 'bridge_path_pedestrian', 'tunnel_path_pedestrian', 'highway_path']);

export function highlightCycleways(map: Pick<Map, 'getStyle' | 'setPaintProperty'>) {
  // Recolor Liberty/Dark path strokes without changing ordering or other path types.
  for (const layer of map.getStyle().layers) {
    if (layer.type !== 'line' || !pathLayers.has(layer.id)) continue;
    const color = layer.paint?.['line-color'] ?? '#ffffff';
    if (typeof color !== 'string' && !Array.isArray(color)) continue;
    map.setPaintProperty(layer.id, 'line-color', [
      'case', ['==', ['get', 'subclass'], 'cycleway'], '#2463eb', color,
    ]);
  }
}
