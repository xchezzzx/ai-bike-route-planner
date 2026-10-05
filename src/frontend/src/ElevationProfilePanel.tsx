import { useEffect, useMemo, useRef } from 'react';
import { Chart, Filler, LineController, LineElement, LinearScale, PointElement, type ChartEvent } from 'chart.js';
import { X } from 'lucide-react';
import { buildElevationProfile, nearestProfileIndex, sampleProfile } from './elevationProfile';
import { quantity, t } from './i18n';
import type { GeneratedRoute, Locale } from './types';

Chart.register(LineController, LineElement, PointElement, LinearScale, Filler);
type Datum = { x: number; y: number | null };

export interface ElevationProfileProps {
  route: GeneratedRoute;
  locale: Locale;
  theme: 'light' | 'dark';
  color: string;
  inspectedIndex: number | null;
  onInspect: (index: number | null) => void;
}

export default function ElevationProfilePanel({ route, locale, theme, color, inspectedIndex, onInspect }: ElevationProfileProps) {
  const profile = useMemo(() => buildElevationProfile(route.geometry), [route.geometry]);
  const data = useMemo(() => sampleProfile(profile.points).map(point => ({ x: point.distanceMeters / 1000, y: point.elevationMeters })), [profile]);
  const canvas = useRef<HTMLCanvasElement>(null);
  const chartRef = useRef<Chart<'line', Datum[]> | null>(null);
  const inspectRef = useRef(onInspect);
  const pointerPosition = useRef<{ x: number; y: number } | null>(null);
  const controlSelectionTime = useRef(-Infinity);
  useEffect(() => { inspectRef.current = onInspect; }, [onInspect]);
  const current = inspectedIndex === null ? null : profile.points[inspectedIndex] ?? null;
  const inspectFromControl = (index: number | null, timeStamp: number) => {
    controlSelectionTime.current = timeStamp;
    onInspect(index);
  };
  const pointText = (index: number) => {
    const point = profile.points[index];
    return `${quantity(locale, point.distanceMeters, 'km', 1000)} · ${quantity(locale, point.elevationMeters, 'm')}`;
  };

  useEffect(() => {
    if (!canvas.current || profile.knownCount === 0) return;
    const style = getComputedStyle(document.documentElement);
    const foreground = style.getPropertyValue('--muted').trim();
    const divider = style.getPropertyValue('--divider').trim();
    let disposed = false;
    const inspect = (event: ChartEvent) => {
      if (disposed) return;
      // Chart.js queues native events until the next frame. Older pointer input
      // must not overwrite a newer selection made directly on the controls.
      if (event.native && event.native.timeStamp < controlSelectionTime.current) return;
      // A redraw/scroll can emit a mouse event without user movement. Keep
      // keyboard inspection until the pointer actually moves or the user taps.
      if (event.type === 'mousemove' && event.native instanceof MouseEvent) {
        const { clientX: x, clientY: y, movementX, movementY } = event.native;
        const previous = pointerPosition.current;
        pointerPosition.current = { x, y };
        if (previous?.x === x && previous.y === y && !(movementX || movementY)) return;
      }
      if (event.x == null || event.y == null || event.x < chart.chartArea.left || event.x > chart.chartArea.right
        || event.y < chart.chartArea.top || event.y > chart.chartArea.bottom) {
        inspectRef.current(null);
        return;
      }
      const distance = chart.scales.x.getValueForPixel(event.x);
      inspectRef.current(distance === undefined ? null : nearestProfileIndex(profile.points, distance * 1000));
    };
    const chart = new Chart<'line', Datum[]>(canvas.current, {
      type: 'line',
      // Updating the inspection dot must not replay an old pointer over keyboard selection.
      plugins: [{ id: 'inspection-events', beforeEvent: (_chart, args) => args.replay ? false : undefined }],
      data: { datasets: [
        { data, borderColor: color, backgroundColor: `${color}22`, fill: 'start', borderWidth: 2,
          pointRadius: context => data[context.dataIndex]?.y != null && data[context.dataIndex - 1]?.y == null && data[context.dataIndex + 1]?.y == null ? 3 : 0,
          spanGaps: false, tension: 0 },
        { data: [], borderColor: color, backgroundColor: color, pointRadius: 5, pointBorderColor: theme === 'dark' ? '#ffffff' : '#202927', pointBorderWidth: 2, showLine: false },
      ] },
      options: {
        responsive: true, maintainAspectRatio: false, animation: false, parsing: false, locale,
        onHover: inspect, onClick: inspect,
        scales: {
          x: { type: 'linear', min: 0, max: profile.distanceMeters > 0 ? profile.distanceMeters / 1000 : 1,
            title: { display: true, text: t(locale, 'distanceInput'), color: foreground }, ticks: { color: foreground, maxTicksLimit: 8 }, grid: { color: divider } },
          y: { type: 'linear', title: { display: true, text: t(locale, 'elevationAxis'), color: foreground },
            ticks: { color: foreground, maxTicksLimit: 6 }, grid: { color: divider } },
        },
      },
    });
    chartRef.current = chart;
    return () => { disposed = true; chartRef.current = null; chart.destroy(); };
  }, [profile, data, locale, theme, color]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart) return;
    chart.data.datasets[1].data = current?.elevationMeters != null ? [{ x: current.distanceMeters / 1000, y: current.elevationMeters }] : [];
    chart.update('none');
  }, [current, profile, locale, theme, color]);

  return <section className="elevation-profile" aria-label={t(locale, 'elevationProfile')}>
    <h2>{t(locale, 'elevationProfile')}</h2>
    <dl className="elevation-summary">
      {([
        ['minimumElevation', profile.minElevationMeters], ['maximumElevation', profile.maxElevationMeters],
        ['ascent', route.ascentMeters], ['descent', route.descentMeters],
      ] as const).map(([label, value]) => <div key={label}><dt>{t(locale, label)}</dt><dd>{quantity(locale, value, 'm')}</dd></div>)}
    </dl>
    {profile.knownCount === 0 ? <p className="elevation-status">{t(locale, 'elevationUnavailable')}</p> : <>
      {profile.knownCount < profile.points.length && <p className="elevation-status">{t(locale, 'elevationPartial')}</p>}
      <div className="elevation-chart">
        <canvas ref={canvas} role="img" aria-label={t(locale, 'elevationProfile')}
          onMouseLeave={() => onInspect(null)} />
      </div>
      <input className="elevation-position" type="range" min={0} max={profile.points.length - 1} step={1}
        value={current?.index ?? 0} aria-label={t(locale, 'trackPoint')} aria-valuetext={pointText(current?.index ?? 0)}
        onFocus={event => inspectFromControl(current?.index ?? 0, event.timeStamp)}
        onChange={event => inspectFromControl(Number(event.target.value), event.timeStamp)}
        onKeyDown={event => { if (event.key === 'Escape') inspectFromControl(null, event.timeStamp); }} />
      <div className="elevation-inspector">
        <output aria-label={t(locale, 'elevationPoint')}>{current ? pointText(current.index) : '—'}</output>
        <button className="icon-button" title={t(locale, 'clearInspection')} aria-label={t(locale, 'clearInspection')}
          disabled={!current} onClick={event => inspectFromControl(null, event.timeStamp)}><X size={16} aria-hidden="true" /></button>
      </div>
    </>}
  </section>;
}
