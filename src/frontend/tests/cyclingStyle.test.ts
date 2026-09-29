import { describe, expect, it, vi } from 'vitest';
import type { LineLayerSpecification, StyleSpecification } from 'maplibre-gl';
import { highlightCycleways } from '../src/cyclingStyle';

describe('Liberty cycling emphasis', () => {
  it('also highlights explicit cycleways in the Dark style without changing other paths', () => {
    const setPaintProperty = vi.fn();
    highlightCycleways({ getStyle: () => ({ version: 8, sources: {}, layers: [{ id: 'highway_path', type: 'line', source: 'openmaptiles', paint: { 'line-color': '#1b1b1d' } }] }), setPaintProperty });
    expect(setPaintProperty).toHaveBeenCalledWith('highway_path', 'line-color', ['case', ['==', ['get', 'subclass'], 'cycleway'], '#2463eb', '#1b1b1d']);
  });
  it('colors only explicit cycleways on roads, bridges and tunnels, preserving other paths', () => {
    const baseColor: NonNullable<LineLayerSpecification['paint']>['line-color'] = ['case', ['==', ['get', 'surface'], 'unpaved'], '#eeeeee', '#ffffff'];
    const layers = ['road_path_pedestrian', 'bridge_path_pedestrian', 'tunnel_path_pedestrian', 'bridge_path_pedestrian_casing'].map(id => ({ id, type: 'line', source: 'openmaptiles',
      'source-layer': 'transportation', paint: { 'line-color': baseColor, 'line-dasharray': [1, 0.7] },
    } as LineLayerSpecification));
    const getStyle = (): StyleSpecification => ({ version: 8, sources: {}, layers });
    const setPaintProperty = vi.fn();
    highlightCycleways({ getStyle, setPaintProperty });
    expect(setPaintProperty.mock.calls).toEqual([
      'road_path_pedestrian', 'bridge_path_pedestrian', 'tunnel_path_pedestrian',
    ].map(id => [id, 'line-color', ['case', ['==', ['get', 'subclass'], 'cycleway'], '#2463eb', baseColor]]));
    // No bicycle=yes/designated inference, casing changes, layer moves or width changes.
  });

  it('tolerates missing or replaced style layers without preventing map use', () => {
    const setPaintProperty = vi.fn();
    highlightCycleways({ getStyle: () => ({ version: 8, sources: {}, layers: [] }), setPaintProperty });
    expect(setPaintProperty).not.toHaveBeenCalled();
  });
});
