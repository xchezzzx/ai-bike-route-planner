import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import RouteMap from '../src/RouteMap';
import { candidates } from './fixtures';

const state = vi.hoisted(() => ({ maps: [] as any[] }));
vi.mock('maplibre-gl', () => ({
  setWorkerUrl: vi.fn(), AttributionControl: class {},
  LngLatBounds: class { extend() { return this; } },
  Marker: class { setLngLat() { return this; } addTo() { return this; } remove() {} },
  Map: class {
    events: Record<string, (event?: any) => void> = {}; layers = new Map(); sources = new Map(); hits: any[] = [];
    fitBounds = vi.fn(); remove = vi.fn(); resize = vi.fn(); setPaintProperty = vi.fn(); setFilter = vi.fn();
    constructor() { state.maps.push(this); }
    on(name: string, fn: (event?: any) => void) { this.events[name] = fn; }
    getCanvas() { return document.createElement('canvas'); } addControl() {} getStyle() { return { layers: [] }; }
    getLayer(id: string) { return this.layers.get(id); } addLayer(layer: any) { this.layers.set(layer.id, layer); }
    getSource(id: string) { return this.sources.get(id); }
    addSource(id: string, source: any) { this.sources.set(id, { ...source, setData: vi.fn().mockResolvedValue(undefined) }); }
    queryRenderedFeatures(_: unknown, opts: { layers: string[] }) { return this.hits.filter(hit => opts.layers.includes(hit.layer.id)); }
  },
}));
beforeEach(() => { state.maps.length = 0; vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} }); });
const routeProps = () => ({ locale: 'en' as const, pick: 'start' as const, candidates: candidates.candidates, selected: 0, onRouteSelect: vi.fn(), onSelect: vi.fn() });
const click = { point: { x: 1, y: 1 }, lngLat: { lat: 32, wrap: () => ({ lng: 34 }) } };

it('switches modes without refitting and restores layers on retry', async () => {
  const props = routeProps(); render(<RouteMap {...props} />);
  const map = state.maps[0]; await act(() => map.events.load());
  expect(map.resize).toHaveBeenCalled();
  expect(map.resize.mock.invocationCallOrder[0]).toBeLessThan(map.fitBounds.mock.invocationCallOrder[0]);
  const fits = map.fitBounds.mock.calls.length; const layerCount = map.layers.size;
  await userEvent.click(screen.getByRole('radio', { name: 'Road type' }));
  await userEvent.click(screen.getByRole('radio', { name: 'Surface' }));
  expect(map.fitBounds).toHaveBeenCalledTimes(fits);
  expect(map.layers.size).toBe(layerCount); expect(state.maps).toHaveLength(1);
  await act(() => map.events.error());
  await userEvent.click(screen.getByRole('button', { name: 'Retry map' }));
  await act(() => state.maps[1].events.load());
  expect(state.maps[1].getLayer('segment-hit')).toBeTruthy();
});
it('inspects the selected hit area before alternatives and never moves endpoints on gaps', async () => {
  const props = routeProps(); const ui = render(<RouteMap {...props} />);
  const map = state.maps[0]; await act(() => map.events.load());
  map.hits = [{ layer: { id: 'segment-hit' }, properties: { segmentIndex: 0, candidateIndex: 0 } }];
  await act(() => map.events.click(click));
  expect(screen.getByRole('region', { name: 'Segment details' })).toHaveTextContent('Unknown surface');
  expect(props.onSelect).not.toHaveBeenCalled(); expect(props.onRouteSelect).not.toHaveBeenCalled();
  ui.rerender(<RouteMap {...props} selected={1} />);
  expect(screen.queryByRole('region', { name: 'Segment details' })).not.toBeInTheDocument();
  map.hits = []; await act(() => map.events.click(click));
  expect(props.onSelect).toHaveBeenCalledWith({ latitude: 32, longitude: 34 });
  ui.rerender(<RouteMap {...props} candidates={[]} />);
  expect(screen.queryByRole('radio', { name: 'Surface' })).not.toBeInTheDocument();
});
