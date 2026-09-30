import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import ElevationProfilePanel from '../src/ElevationProfilePanel';
import { route } from './fixtures';
import type { ElevationProfileProps } from '../src/ElevationProfilePanel';

const state = vi.hoisted(() => ({ charts: [] as any[] }));
vi.mock('chart.js', () => {
  class Chart {
    static register = vi.fn();
    data: any; options: any; plugins: any;
    chartArea = { left: 0, right: 100, top: 0, bottom: 100 };
    scales = { x: { getValueForPixel: (pixel: number) => pixel / 100 * this.data.datasets[0].data.at(-1).x } };
    update = vi.fn(); destroy = vi.fn();
    constructor(_canvas: unknown, config: any) { this.data = config.data; this.options = config.options; this.plugins = config.plugins; state.charts.push(this); }
  }
  return { Chart, LineController: {}, LineElement: {}, PointElement: {}, LinearScale: {}, Filler: {} };
});
beforeEach(() => { state.charts = []; });
const elevated = { ...route, ascentMeters: 35, descentMeters: 20, geometry: route.geometry.map((point, index) => ({ ...point, elevationMeters: [-10, 0, null, 25][index] })) };
function props(overrides: Partial<ElevationProfileProps> = {}): ElevationProfileProps {
  return { route: elevated, locale: 'en', theme: 'light', color: '#15724f', inspectedIndex: null, onInspect: vi.fn(), ...overrides };
}
it('renders true zero/negative heights, gaps and provider totals without bridging', () => {
  render(<ElevationProfilePanel {...props()} />);
  expect(screen.getByRole('region', { name: 'Elevation profile' })).toBeInTheDocument();
  expect(screen.getByText('-10 m')).toBeInTheDocument();
  expect(screen.getByText('25 m')).toBeInTheDocument();
  expect(screen.getByText('35 m')).toBeInTheDocument();
  expect(screen.getByText('Elevation data is incomplete.')).toBeInTheDocument();
  const chart = state.charts[0];
  expect(chart.data.datasets[0].data.map((p: any) => p.y)).toEqual([-10, 0, null, 25]);
  expect(chart.data.datasets[0].spanGaps).toBe(false);
  expect(chart.data.datasets[0].tension).toBe(0);
});
it('keeps missing heights missing and avoids a misleading chart or inspector', () => {
  render(<ElevationProfilePanel {...props({ route })} />);
  expect(screen.getByText('Elevation data is unavailable.')).toBeInTheDocument();
  expect(screen.queryByRole('slider')).not.toBeInTheDocument();
  expect(screen.queryByRole('img', { name: 'Elevation profile' })).not.toBeInTheDocument();
  expect(state.charts).toHaveLength(0);
});
it('shows isolated known heights and prevents hover replay from undoing keyboard inspection', () => {
  const isolated = { ...route, geometry: route.geometry.map((point, index) => ({ ...point, elevationMeters: index === 1 ? 0 : null })) };
  render(<ElevationProfilePanel {...props({ route: isolated })} />);
  const chart = state.charts[0];
  const radius = chart.data.datasets[0].pointRadius;
  expect(typeof radius).toBe('function');
  expect(radius({ dataIndex: 1 })).toBe(3);
  expect(radius({ dataIndex: 0 })).toBe(0);
  expect(chart.plugins[0].beforeEvent(chart, { replay: true })).toBe(false);
  expect(chart.plugins[0].beforeEvent(chart, { replay: false })).not.toBe(false);
});
it('emits original indices from chart hover/click and keyboard inspection, including unknown points', () => {
  const options = props();
  const { rerender } = render(<ElevationProfilePanel {...options} />);
  const chart = state.charts[0];
  chart.options.onHover({ type: 'mousemove', x: 0, y: 50 });
  expect(options.onInspect).toHaveBeenLastCalledWith(0);
  chart.options.onClick({ type: 'click', x: 100, y: 50 });
  expect(options.onInspect).toHaveBeenLastCalledWith(3);
  chart.options.onHover({ type: 'mousemove', x: 101, y: 50 });
  expect(options.onInspect).toHaveBeenLastCalledWith(null);
  fireEvent.change(screen.getByRole('slider', { name: 'Track point' }), { target: { value: '2' } });
  expect(options.onInspect).toHaveBeenLastCalledWith(2);
  rerender(<ElevationProfilePanel {...options} inspectedIndex={2} />);
  expect(screen.getByRole('slider')).toHaveAttribute('aria-valuetext', expect.stringContaining('Unknown'));
  expect(chart.data.datasets[1].data).toEqual([]);
  fireEvent.keyDown(screen.getByRole('slider'), { key: 'Escape' });
  expect(options.onInspect).toHaveBeenLastCalledWith(null);
  expect(state.charts).toHaveLength(1);
});
it('updates the selected dot without rebuilding, destroys old charts and ignores stale events', () => {
  const options = props();
  const { rerender, unmount } = render(<ElevationProfilePanel {...options} />);
  const first = state.charts[0];
  rerender(<ElevationProfilePanel {...options} inspectedIndex={1} />);
  expect(state.charts).toHaveLength(1);
  expect(first.data.datasets[1].data[0]).toMatchObject({ y: 0 });
  expect(first.update).toHaveBeenCalledWith('none');
  rerender(<ElevationProfilePanel {...options} inspectedIndex={1} theme="dark" locale="he" />);
  expect(first.destroy).toHaveBeenCalledOnce();
  expect(state.charts).toHaveLength(2);
  const second = state.charts[1];
  expect(second.data.datasets[1].data[0]).toMatchObject({ y: 0 });
  unmount();
  expect(second.destroy).toHaveBeenCalledOnce();
  const calls = (options.onInspect as ReturnType<typeof vi.fn>).mock.calls.length;
  second.options.onClick({ type: 'click', x: 50, y: 50 });
  expect(options.onInspect).toHaveBeenCalledTimes(calls);
});
