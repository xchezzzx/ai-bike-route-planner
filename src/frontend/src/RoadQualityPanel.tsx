import { codeText, quantity, t } from './i18n';
import type { ExcludedCandidate, Locale, RoadQuality } from './types';

export function RouteQuality({ quality: q, locale }: { quality: RoadQuality; locale: Locale }) {
  const number = (meters: number) => quantity(locale, meters, 'km', 1000);
  return <section className="quality-section" aria-label={t(locale, 'roadQuality')}>
    <h3>{t(locale, 'roadQuality')}</h3>
    <p>{t(locale, q.surfaceEvidenceState === 'complete' ? 'surfaceComplete' : q.surfaceEvidenceState === 'partial' ? 'surfacePartial' : 'surfaceUnavailable')}</p>
    <dl className="quality-metrics">
      <div><dt>{t(locale, 'surfacePaved')}</dt><dd dir="ltr">{number(q.surface.pavedMeters)}</dd></div>
      <div><dt>{t(locale, 'surfaceNonRoad')}</dt><dd dir="ltr">{number(q.surface.nonRoadMeters)}</dd></div>
      <div><dt>{t(locale, 'surfaceOther')}</dt><dd dir="ltr">{number(q.surface.otherKnownMeters)}</dd></div>
      <div><dt>{t(locale, 'surfaceUnknown')}</dt><dd dir="ltr">{number(q.surface.unknownMeters)}</dd></div>
      <div><dt>{t(locale, 'repeatedDistance')}</dt><dd dir="ltr">{quantity(locale, q.repeatedMeters, 'm')}</dd></div>
      <div><dt>{t(locale, 'sharedStem')}</dt><dd dir="ltr">{quantity(locale, q.sharedStemMeters, 'm')}</dd></div>
      <div><dt>{t(locale, 'remainingRepeats')}</dt><dd dir="ltr">{quantity(locale, q.remainingRepeatedMeters, 'm')}</dd></div>
    </dl>
    <details><summary>{t(locale, 'roadTypes')}</summary><dl className="quality-metrics">
      {(['unknown', 'stateRoad', 'road', 'street', 'path', 'track', 'cycleway', 'footway', 'steps', 'ferry', 'construction'] as const).map(key =>
        <div key={key}><dt>{codeText(locale, `way_${key}`)}</dt><dd dir="ltr">{number(q.ways[`${key}Meters`])}</dd></div>)}
    </dl></details>
  </section>;
}

export function ExcludedRoutes({ candidates, locale }: { candidates: ExcludedCandidate[]; locale: Locale }) {
  if (!candidates.length) return null;
  return <details className="excluded-routes"><summary>{t(locale, 'excludedRoutes')}</summary>
    <ul>{candidates.map(candidate => <li key={candidate.seed}>
      <strong>{t(locale, 'seed')} {candidate.seed}: <span dir="ltr">{quantity(locale, candidate.distanceMeters, 'km', 1000)}</span></strong>
      <ul>{candidate.reasons.map(reason => <li key={reason}>{codeText(locale, reason)}</li>)}</ul>
    </li>)}</ul>
  </details>;
}
