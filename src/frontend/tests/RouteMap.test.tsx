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
    setStyle = vi.fn(() => { this.layers.clear(); this.sources.clear(); });
    loaded() { return true; }
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
const click = { point: { x: 1, y: 1 }, lngLat: { lat: 32, wrap: () => ({ lng: 34 }) }, originalEvent: { preventDefault: vi.fn() } };
async function loadMap(map: any) {
  await act(() => map.events['style.load']());
  await act(() => map.events.idle());
}

it('opens endpoint actions after both points are set instead of moving either', async () => {
  const props = { ...routeProps(), candidates: [], shape: 'pointToPoint' as const, start: { latitude: 32, longitude: 34 }, destination: { latitude: 32.1, longitude: 34.1 }, onEndpointSelect: vi.fn() };
  render(<RouteMap {...props} />); const map = state.maps[0]; await loadMap(map);
  await act(() => map.events.click(click));
  expect(props.onSelect).not.toHaveBeenCalled();
  expect(screen.getByRole('menu', { name: 'Choose endpoint' })).toBeInTheDocument();
  await userEvent.click(screen.getByRole('menuitem', { name: 'To here' }));
  expect(props.onEndpointSelect).toHaveBeenCalledWith('destination', { latitude: 32, longitude: 34 });
  expect(screen.queryByRole('menu')).not.toBeInTheDocument();
});
it('right click opens actions even when no endpoints exist, escape cancels', async () => {
  const props = routeProps(); render(<RouteMap {...props} />); const map = state.maps[0]; await loadMap(map);
  await act(() => map.events.contextmenu(click));
  expect(screen.getByRole('menuitem', { name: 'From here' })).toBeInTheDocument();
  await userEvent.keyboard('{Escape}');
  expect(screen.queryByRole('menu')).not.toBeInTheDocument(); expect(props.onSelect).not.toHaveBeenCalled();
});
it('keeps an endpoint menu during programmatic resize but dismisses it on a user pan', async () => {
  render(<RouteMap {...routeProps()} />); const map = state.maps[0]; await loadMap(map);
  await act(() => map.events.contextmenu(click));
  await act(() => map.events.movestart({}));
  expect(screen.getByRole('menuitem', { name: 'To here' })).toBeInTheDocument();
  await act(() => map.events.movestart({ originalEvent: new MouseEvent('mousedown') }));
  expect(screen.queryByRole('menu')).not.toBeInTheDocument();
});

it('switches modes without refitting and restores layers on retry', async () => {
  const props = routeProps(); render(<RouteMap {...props} />);
  const map = state.maps[0]; await loadMap(map);
  expect(map.getLayer('segment-hit').source).toBe('track-segment-hits');
  expect(map.getSource('track-segment-hits').data.features[0].properties.segmentIndex).toBe(0);
  expect(map.resize).toHaveBeenCalled();
  expect(map.resize.mock.invocationCallOrder[0]).toBeLessThan(map.fitBounds.mock.invocationCallOrder[0]);
  const fits = map.fitBounds.mock.calls.length; const layerCount = map.layers.size;
  await userEvent.click(screen.getByRole('radio', { name: 'Road type' }));
  await userEvent.click(screen.getByRole('radio', { name: 'Surface' }));
  expect(map.fitBounds).toHaveBeenCalledTimes(fits);
  expect(map.layers.size).toBe(layerCount); expect(state.maps).toHaveLength(1);
  await act(() => map.events.error());
  await userEvent.click(screen.getByRole('button', { name: 'Retry map' }));
  await loadMap(state.maps[1]);
  expect(state.maps[1].getLayer('segment-hit')).toBeTruthy();
});
it('inspects the selected hit area before alternatives and never moves endpoints on gaps', async () => {
  const props = routeProps(); const ui = render(<RouteMap {...props} />);
  const map = state.maps[0]; await loadMap(map);
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

it('restores overlays on style load without refitting or clearing the inspected segment', async () => {
  const props = routeProps(); const ui = render(<RouteMap {...props} theme="light" />);
  const map = state.maps[0]; await loadMap(map);
  map.hits = [{ layer: { id: 'segment-hit' }, properties: { segmentIndex: 0, candidateIndex: 0 } }];
  await act(() => map.events.click(click));
  const fits = map.fitBounds.mock.calls.length;
  ui.rerender(<RouteMap {...props} theme="dark" />);
  expect(map.setStyle).toHaveBeenCalledWith('https://tiles.openfreemap.org/styles/dark', { diff: false });
  await loadMap(map);
  expect(state.maps).toHaveLength(1);
  expect(map.fitBounds).toHaveBeenCalledTimes(fits);
  expect(map.getLayer('segment-hit')).toBeTruthy();
  expect(map.getSource('track-segments').data.features).not.toHaveLength(0);
  expect(screen.getByRole('region', { name: 'Segment details' })).toBeInTheDocument();
});
