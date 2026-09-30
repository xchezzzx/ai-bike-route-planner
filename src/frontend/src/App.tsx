import { useEffect, useRef, useState } from 'react';
import { useTheme } from 'next-themes';
import { Bike, Check, Download, LoaderCircle, LocateFixed, MapPin, RefreshCw, Route, Search, Square, X } from 'lucide-react';
import { request } from './api';
import { codeText, fieldText, quantity, t, type MessageKey } from './i18n';
import { readCoordinate } from './request';
import RouteMap from './RouteMap';
import ThemeControl from './ThemeControl';
import StartLocation from './StartLocation';
import TargetInputs from './TargetInputs';
import { trackName } from './trackName';
import type { CoordinateInput, Draft, Locale } from './types';
import { usePlanner } from './usePlanner';
import { ExcludedRoutes, RouteQuality } from './RoadQualityPanel';

export default function App() {
  const { resolvedTheme } = useTheme();
  const planner = usePlanner();
  const { inputs, update, interpretation, intent, pending, error, results, selected } = planner;
  const locale = inputs.locale;
  const text = (key: MessageKey) => t(locale, key);
  const [pick, setPick] = useState<'start' | 'destination'>('start');
  const [health, setHealth] = useState<MessageKey>('apiChecking');
  const healthRequest = useRef<AbortController | null>(null);
  const chosen = results?.candidates[selected];
  const isLoop = inputs.manual.shape === 'loop';
  const activePick = isLoop ? 'start' : pick;

  async function checkHealth() {
    healthRequest.current?.abort();
    const controller = new AbortController();
    healthRequest.current = controller;
    setHealth('apiChecking');
    try { await request('/health', undefined, controller.signal, 5000); if (!controller.signal.aborted) setHealth('apiOnline'); }
    catch { if (!controller.signal.aborted) setHealth('apiOffline'); }
  }
  useEffect(() => { void checkHealth(); return () => healthRequest.current?.abort(); }, []);
  useEffect(() => { document.documentElement.lang = locale; document.documentElement.dir = locale === 'he' ? 'rtl' : 'ltr'; document.title = t(locale, 'app'); }, [locale]);

  function point(field: 'start' | 'destination') {
    try { return readCoordinate(inputs[field], field); } catch { return undefined; }
  }
  function download() {
    if (!chosen) return;
    const url = URL.createObjectURL(new Blob([chosen.route.gpx], { type: 'application/gpx+xml;charset=utf-8' }));
    const anchor = document.createElement('a');
    anchor.href = url; anchor.download = `${trackName(chosen.route, selected)}.gpx`;
    document.body.append(anchor); anchor.click(); anchor.remove();
    const revoke = URL.revokeObjectURL.bind(URL);
    setTimeout(() => revoke(url), 1000);
  }
  function coordinates(field: 'start' | 'destination') {
    const value = inputs[field];
    const labels = field === 'start' ? ['startLatitude', 'startLongitude'] as const : ['destinationLatitude', 'destinationLongitude'] as const;
    return <fieldset className="coordinate-fields">
      <legend><span className={`point-dot ${field}`} />{text(field)}</legend>
      <div className="coordinate-row">
        {(['latitude', 'longitude'] as const).map((key, index) => <label key={key}>
          <span>{text(key)}</span>
          <input dir="ltr" inputMode="decimal" aria-label={text(labels[index])} value={value[key]} placeholder={key === 'latitude' ? '±90' : '±180'} onChange={event => update({ [field]: { ...value, [key]: event.target.value } as CoordinateInput })} />
        </label>)}
        <button className="icon-button clear-coordinate" type="button" title={text(field === 'start' ? 'clearStart' : 'clearDestination')} aria-label={text(field === 'start' ? 'clearStart' : 'clearDestination')} onClick={() => update({ [field]: { latitude: '', longitude: '' } })}><X size={16} /></button>
      </div>
    </fieldset>;
  }
  return <>
    <header className="app-header">
      <h1><Bike size={24} aria-hidden="true" />{text('app')}</h1>
      <div className="header-actions">
        <ThemeControl locale={locale} />
        <div className={`health ${health}`} role="status"><span className="health-dot" /><span>{text(health)}</span><button type="button" className="icon-button" title={text('checkHealth')} aria-label={text('checkHealth')} onClick={() => void checkHealth()}><RefreshCw size={15} /></button></div>
        <label className="language"><span className="sr-only">{text('language')}</span><select aria-label={text('language')} dir="ltr" value={locale} onChange={event => update({ locale: event.target.value as Locale })}><option value="en">EN</option><option value="ru">RU</option><option value="he">HE</option></select></label>
      </div>
    </header>
    <main className="workspace">
      <aside className="controls" aria-label={text('request')}>
        <section className="control-section">
          <div className="coordinates-heading"><h2><MapPin size={17} />{text('coordinates')}</h2><button type="button" className="icon-button" title={text('useLocation')} aria-label={text('useLocation')} onClick={planner.location.locate}><LocateFixed size={18} /></button></div>
          <StartLocation locale={locale} location={planner.location} />
          <fieldset className="segmented"><legend className="sr-only">{text('shape')}</legend>{(['loop', 'pointToPoint'] as const).map(shape => <label key={shape}><input type="radio" name="shape" checked={inputs.manual.shape === shape} onChange={() => { setPick('start'); update({ manual: { ...inputs.manual, shape } }); }} /><span>{codeText(locale, shape)}</span></label>)}</fieldset>
          {!isLoop && <fieldset className="segmented"><legend className="sr-only">{text('pickPoint')}</legend>{(['start', 'destination'] as const).map(field => <label key={field}><input type="radio" name="pick" checked={activePick === field} onChange={() => setPick(field)} /><span>{text(field)}</span></label>)}</fieldset>}
          {coordinates('start')}{!isLoop && coordinates('destination')}
        </section>
        <section className="control-section">
          <fieldset className="segmented"><legend className="sr-only">{text('inputMode')}</legend>{(['prompt', 'manual'] as const).map(mode => <label key={mode}><input type="radio" name="mode" checked={inputs.mode === mode} onChange={() => update({ mode })} /><span>{text(mode === 'prompt' ? 'promptMode' : 'manualMode')}</span></label>)}</fieldset>
          {inputs.mode === 'prompt' ? <label className="prompt-label">{text('prompt')}<textarea rows={4} maxLength={4000} value={inputs.prompt} onChange={event => update({ prompt: event.target.value })} /></label> : <div className="manual-fields">
            {(['profile', 'elevation'] as const).map(key => <label key={key}>{text(key)}<select aria-label={text(key)} value={inputs.manual[key]} onChange={event => update({ manual: { ...inputs.manual, [key]: event.target.value } })}>{(key === 'profile' ? ['road', 'gravel'] : ['balanced', 'minimize', 'seekClimbs']).map(value => <option key={value} value={value} disabled={value === 'gravel' || key === 'elevation' && !isLoop && value !== 'balanced'}>{codeText(locale, value)}</option>)}</select></label>)}
            <TargetInputs locale={locale} value={inputs.manual} onChange={manual => update({ manual })} />
          </div>}
          <button className="secondary wide" type="button" disabled={!!pending} onClick={() => void planner.prepare()}><Search size={17} />{text(inputs.mode === 'prompt' ? 'interpret' : 'validate')}</button>
        </section>
        {interpretation && <section className="control-section interpretation" aria-label={text('draft')}>
          <h2>{text(interpretation.status)}</h2>
          <DraftSummary draft={interpretation.draft} locale={locale} />
          {interpretation.clarifications.length > 0 && <ul className="notice-list">{interpretation.clarifications.map((item, index) => <li key={index}>{item.message}</li>)}</ul>}
          <Notices codes={interpretation.limitations} locale={locale} title="limitations" />
          <Notices codes={interpretation.assumptions} locale={locale} title="assumptions" />
        </section>}
        <div className="generate-section">
          {intent?.shape === 'loop' && intent.profile === 'road' && <label className="refinement-toggle"><input type="checkbox" checked={planner.refine} onChange={event => planner.setRefine(event.target.checked)} />{text('refinement')}</label>}
          {!intent && !pending && <p id="generation-state" className="generation-state" role="status">{text(interpretation?.status ?? 'notValidated')}</p>}
          <button className="primary wide" type="button" aria-describedby={!intent && !pending ? 'generation-state' : undefined} disabled={!intent || !!pending} onClick={() => void planner.generate()}><Route size={18} />{text('generate')}</button>
          {pending && <div className="pending" role="status"><LoaderCircle className="spinner" size={17} /><span>{text(pending)}</span><button type="button" className="icon-button" title={text('cancel')} aria-label={text('cancel')} onClick={planner.cancel}><Square size={16} /></button></div>}
          {error && <div className={error.code === 'cancelled' ? 'notice' : 'error'} role={error.code === 'cancelled' ? 'status' : 'alert'}><p>{codeText(locale, error.code, 'server_error')}</p>{Object.entries(error.fields).map(([field, codes]) => <p key={field}><strong>{fieldText(locale, field)}: </strong>{codes.map(code => codeText(locale, code, 'invalid_value')).join(' ')}</p>)}</div>}
        </div>
      </aside>
      <div className="map-and-results">
        <RouteMap locale={locale} theme={resolvedTheme === 'dark' ? 'dark' : 'light'} start={point('start')} destination={isLoop ? undefined : point('destination')} focus={planner.locationCenter} pick={activePick} candidates={results?.candidates ?? []} selected={selected} onRouteSelect={planner.select} onSelect={coordinate => update({ [activePick]: { latitude: coordinate.latitude.toFixed(6), longitude: coordinate.longitude.toFixed(6) } })} />
        {results ? <section className="results" aria-label={text('routes')}>
          <div className="results-heading"><h2>{text('routes')} <span className="count">{results.candidates.length}</span></h2>{chosen && <button type="button" className="icon-button download" title={text('download')} aria-label={text('download')} onClick={download}><Download size={20} /><span dir="ltr">GPX</span></button>}</div>
          {chosen ? <>
          <div role="radiogroup" aria-label={text('routes')} className="route-options">{results.candidates.map((candidate, index) => <label className={`route-option ${selected === index ? 'selected' : ''}`} key={candidate.seed}>
            <input type="radio" name="route" checked={selected === index} onChange={() => planner.select(index)} />
            <span className={`route-swatch color-${index % 3}`} /><span className="route-label"><span>{text('route')} {index + 1}</span>{candidate.route.name && <small dir="ltr">{trackName(candidate.route, index)}</small>}</span><b dir="ltr">{quantity(locale, candidate.route.distanceMeters, 'km', 1000)}</b>
          </label>)}</div>
          <dl className="route-metrics"><div><dt>{text('duration')}</dt><dd dir="ltr">{quantity(locale, chosen.route.estimatedDurationSeconds, 'min', 60)}</dd></div><div><dt>{text('ascent')}</dt><dd dir="ltr">{quantity(locale, chosen.route.ascentMeters, 'm')}</dd></div><div><dt>{text('descent')}</dt><dd dir="ltr">{quantity(locale, chosen.route.descentMeters, 'm')}</dd></div><div><dt>{text('attempts')}</dt><dd dir="ltr">{results.attemptedCount}</dd></div></dl>
          <p className="target-match">{chosen.assessment?.targetsMatched && <Check size={16} />}{text(chosen.assessment ? chosen.assessment.targetsMatched ? 'matched' : 'notMatched' : 'notAssessed')}</p>
          {chosen.assessment && <dl className="comparison"><div><dt>{text('searchDistance')}</dt><dd dir="ltr">{quantity(locale, results.requestedLengthMeters, 'km', 1000)}</dd></div>{chosen.assessment.distanceDeltaMeters != null && <div><dt>{text('distanceDelta')}</dt><dd dir="ltr">{quantity(locale, chosen.assessment.distanceDeltaMeters, 'km', 1000)}</dd></div>}{chosen.assessment.durationDeltaSeconds != null && <div><dt>{text('durationDelta')}</dt><dd dir="ltr">{quantity(locale, chosen.assessment.durationDeltaSeconds, 'min', 60)}</dd></div>}</dl>}
          {chosen.assessment?.quality && <RouteQuality quality={chosen.assessment.quality} locale={locale} />}
          </> : <div role="status"><p>{text('noMatches')}</p><p>{text('attempts')}: {results.attemptedCount}</p></div>}
          <Notices codes={[...results.warnings.filter(code => code !== 'no_candidate_meets_requirements'), ...(chosen?.route.warnings ?? [])]} locale={locale} title="warnings" />
          <ExcludedRoutes candidates={results.excludedCandidates} locale={locale} />
          <Notices codes={results.assumptions} locale={locale} title="assumptions" />
          {planner.planning && <div className="planning-summary">
            <p role="status">{codeText(locale, `advisor_${planner.planning.advisorStatus}`)}</p>
            {planner.planning.advisorFailure && <p>{codeText(locale, `advisor_error_${planner.planning.advisorFailure}`)}</p>}
            <details><summary>{text('searchDetails')}</summary>
              <p>{text('advisorCalls')}: {planner.planning.advisorCallCount}</p>
              <ol className="attempt-list">{planner.planning.attempts.map(attempt => <li key={attempt.seed}>
                <span>{text('seed')} <b dir="ltr">{attempt.seed}</b></span>
                <span dir="ltr">{quantity(locale, attempt.requestedLengthMeters, 'km', 1000)}</span>
                <span>{codeText(locale, `search_reason_${attempt.reason}`)}</span>
                <span>{codeText(locale, `search_outcome_${attempt.outcome}`)}</span>
                {attempt.failure && <span>{codeText(locale, attempt.failure)}</span>}
              </li>)}</ol>
            </details>
          </div>}
          <p className="safety">{text('safety')}</p>{chosen && <p className="route-attribution" dir="auto">{chosen.route.attribution}</p>}
        </section> : <div className="empty-state">{text('empty')}</div>}
      </div>
    </main>
  </>;
}

function Notices({ codes, locale, title }: { codes: string[]; locale: Locale; title: MessageKey }) {
  return codes.length ? <div className="notices"><h3>{t(locale, title)}</h3><ul className="notice-list">{[...new Set(codes)].map(code => <li key={code}>{codeText(locale, code)}</li>)}</ul></div> : null;
}
function DraftSummary({ draft, locale }: { draft: Draft; locale: Locale }) {
  return <dl className="draft-summary">
    {(['shape', 'profile', 'elevation'] as const).map(key => <div key={key}><dt>{t(locale, key)}</dt><dd>{draft[key] ? codeText(locale, draft[key]!) : t(locale, 'notSpecified')}</dd></div>)}
    {(['start', 'destination'] as const).map(key => <div key={key}><dt>{t(locale, key)}</dt><dd dir="ltr">{draft[key] ? `${draft[key]!.latitude}, ${draft[key]!.longitude}` : t(locale, 'notSpecified')}</dd></div>)}
    <div><dt>{t(locale, 'targetDistanceMeters')}</dt><dd dir="ltr">{draft.targetDistanceRangeMeters ? `${quantity(locale, draft.targetDistanceRangeMeters.min, 'km', 1000)} – ${quantity(locale, draft.targetDistanceRangeMeters.max, 'km', 1000)}` : draft.targetDistanceMeters == null ? t(locale, 'notSpecified') : quantity(locale, draft.targetDistanceMeters, 'km', 1000)}</dd></div>
    <div><dt>{t(locale, 'targetDurationSeconds')}</dt><dd dir="ltr">{draft.targetDurationRangeSeconds ? `${quantity(locale, draft.targetDurationRangeSeconds.min, 'min', 60)} – ${quantity(locale, draft.targetDurationRangeSeconds.max, 'min', 60)}` : draft.targetDurationSeconds == null ? t(locale, 'notSpecified') : quantity(locale, draft.targetDurationSeconds, 'min', 60)}</dd></div>
  </dl>;
}
