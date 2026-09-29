import { t } from './i18n';
import type { Locale, ManualInput } from './types';

export default function TargetInputs({ locale, value, onChange }: { locale: Locale; value: ManualInput; onChange: (value: ManualInput) => void }) {
  return <div className="target-fields">{(['distance', 'duration'] as const).map(metric => {
    const label = t(locale, metric === 'distance' ? 'distanceInput' : 'durationInput');
    const mode = value[`${metric}Mode`] ?? 'exact';
    return <fieldset className="target-input" key={metric}>
      <legend>{label}</legend>
      <div className="segmented">{(['exact', 'range'] as const).map(option => <label key={option}>
        <input type="radio" name={`${metric}Mode`} checked={mode === option} onChange={() => onChange({ ...value, [`${metric}Mode`]: option })} />
        <span>{t(locale, option === 'exact' ? 'targetMode' : 'rangeMode')}</span>
      </label>)}</div>
      {mode === 'exact' ? <input dir="ltr" inputMode="decimal" aria-label={label} value={value[metric]} onChange={event => onChange({ ...value, [metric]: event.target.value })} />
        : <div className="range-bounds">{(['Min', 'Max'] as const).map(bound => <label key={bound}>
          {t(locale, bound === 'Min' ? 'minimum' : 'maximum')}
          <input dir="ltr" inputMode="decimal" value={value[`${metric}${bound}`] ?? ''} onChange={event => onChange({ ...value, [`${metric}${bound}`]: event.target.value })} />
        </label>)}</div>}
    </fieldset>;
  })}</div>;
}
