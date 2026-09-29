import { useId } from 'react';
import { t, type MessageKey } from './i18n';
import { segmentPattern, type SegmentDisplayMode } from './routeSegmentStyle';
import type { SegmentData } from './routeSegments';
import type { Locale, RouteSegment } from './types';

interface Props extends SegmentData {
  locale: Locale; mode: SegmentDisplayMode; onModeChange: (mode: SegmentDisplayMode) => void;
  selectedSegmentIndex: number | null; onSegmentSelect: (index: number) => void; color: string;
}
const surfaceLabels: Record<RouteSegment['surface'], MessageKey> = { asphalt: 'segmentAsphalt', paved: 'segmentPaved', unpaved: 'surfaceNonRoad', other: 'surfaceOther', unknown: 'surfaceUnknown' };
const wayLabels: Record<RouteSegment['wayType'], MessageKey> = { stateRoad: 'segmentStateRoad', road: 'segmentRoad', street: 'segmentStreet', path: 'segmentPath', track: 'segmentTrack',
  cycleway: 'segmentCycleway', footway: 'segmentFootway', steps: 'segmentSteps', ferry: 'segmentFerry', construction: 'segmentConstruction', unknown: 'segmentUnknownWay' };

export default function RouteSegmentControls(props: Props) {
  const id = useId();
  const { locale, segments, mode, color, selectedSegmentIndex } = props;
  if (!segments.length) return null;
  const active = selectedSegmentIndex === null ? undefined : segments[selectedSegmentIndex];
  const legend = [...new Map(segments.map(segment => [segment[mode], segment])).values()];
  const label = (segment: RouteSegment) => t(locale, (mode === 'surface' ? surfaceLabels : wayLabels)[segment[mode] as never]);
  return <section className="segment-controls" aria-label={t(locale, 'segmentDisplay')}>
    <fieldset className="segmented segment-modes"><legend className="sr-only">{t(locale, 'segmentDisplay')}</legend>
      {(['surface', 'wayType'] as const).map(value => <label key={value}><input type="radio" name={id} checked={mode === value} onChange={() => props.onModeChange(value)} /><span>{t(locale, value === 'surface' ? 'segmentSurface' : 'segmentWayType')}</span></label>)}
    </fieldset>
    <ul className="segment-legend" aria-label={t(locale, 'segmentLegend')}>{legend.map(segment => {
      const pattern = segmentPattern(segment, mode);
      return <li key={segment[mode]}><svg width="56" height="14" viewBox="0 0 56 14" aria-hidden="true">
        <path d="M0 7H56" stroke="#43534d" strokeWidth="7" />
        <path d="M0 7H56" stroke="white" strokeWidth="5" />
        <path d="M0 7H56" stroke={color} strokeWidth="4" strokeDasharray={pattern.dashArray?.map(n => n * 4).join(' ')} />
        {(pattern.centerStripe || pattern.caution) && <path d="M0 7H56" stroke={pattern.caution ? '#e29d22' : 'white'} strokeWidth="1.2" />}
      </svg><span>{label(segment)}</span></li>;
    })}</ul>
    {props.status !== 'valid' && <p className="segment-data-status" role="status">{t(locale, 'segmentUnavailable')}</p>}
    <details><summary>{t(locale, 'segmentList')}</summary>
      <label className="segment-picker">{t(locale, 'segment')}<select value={selectedSegmentIndex ?? ''} onChange={event => { if (event.target.value !== '') props.onSegmentSelect(Number(event.target.value)); }}>
        <option value="" disabled>{t(locale, 'segmentChoose')}</option>
        {segments.map((segment, index) => <option key={index} value={index}>{index + 1}. {t(locale, surfaceLabels[segment.surface])} / {t(locale, wayLabels[segment.wayType])}</option>)}
      </select></label>
    </details>
    {active && <section className="segment-detail" aria-label={t(locale, 'segmentDetails')} aria-live="polite">
      <strong>{t(locale, 'segment')} {(selectedSegmentIndex ?? 0) + 1}</strong>
      <dl><div><dt>{t(locale, 'segmentSurface')}</dt><dd>{t(locale, surfaceLabels[active.surface])}</dd></div>
        <div><dt>{t(locale, 'segmentWayType')}</dt><dd>{t(locale, wayLabels[active.wayType])}</dd></div></dl>
    </section>}
  </section>;
}
