import { useState } from 'react';
import * as Slider from '@radix-ui/react-slider';
import { Plus, X } from 'lucide-react';
import { quantity, t } from './i18n';
import type { Locale, ManualInput } from './types';

type Metric = 'distance' | 'duration';
const defaults: Record<Metric, [number, number]> = { distance: [35, 45], duration: [60, 120] };

function readRange(value: ManualInput, metric: Metric): [number, number] | undefined {
  const min = Number(value[`${metric}Min`]);
  const max = Number(value[`${metric}Max`]);
  return Number.isFinite(min) && Number.isFinite(max) && min > 0 && min <= max ? [min, max] : undefined;
}

export default function TargetInputs({ locale, value, onChange }: { locale: Locale; value: ManualInput; onChange: (value: ManualInput) => void }) {
  return <div className="target-fields">{(['distance', 'duration'] as const).map(metric => {
    const label = t(locale, metric === 'distance' ? 'distanceInput' : 'durationInput');
    const mode = value[`${metric}Mode`] ?? 'exact';
    function changeMode(option: 'exact' | 'range') {
      const bounds = option === 'range' && !readRange(value, metric) ? defaults[metric] : undefined;
      onChange({ ...value, [`${metric}Mode`]: option,
        ...(bounds ? { [`${metric}Min`]: String(bounds[0]), [`${metric}Max`]: String(bounds[1]) } : {}) });
    }
    return <fieldset className="target-input" key={metric}>
      <legend>{label}</legend>
      <div className="segmented">{(['exact', 'range'] as const).map(option => <label key={option}>
        <input type="radio" name={`${metric}Mode`} checked={mode === option} onChange={() => changeMode(option)} />
        <span>{t(locale, option === 'exact' ? 'targetMode' : 'rangeMode')}</span>
      </label>)}</div>
      {mode === 'exact' ? <input dir="ltr" inputMode="decimal" aria-label={label} value={value[metric]} onChange={event => onChange({ ...value, [metric]: event.target.value })} />
        : <RangeTarget locale={locale} metric={metric} value={value} onChange={onChange} />}
    </fieldset>;
  })}</div>;
}

function RangeTarget({ locale, metric, value, onChange }: { locale: Locale; metric: Metric; value: ManualInput; onChange: (value: ManualInput) => void }) {
  const selected = readRange(value, metric);
  const bounds = selected ?? defaults[metric];
  // Keep the scale stable while dragging, including existing extended ranges.
  const [limits] = useState(() => ({ min: Math.min(1, bounds[0]), max: Math.max(metric === 'distance' ? 100 : 480, Math.ceil(bounds[1])) }));
  const unit = metric === 'distance' ? 'km' : 'min';
  const action = selected ? 'clearRange' : 'setRange';
  function changeBounds(next: number[]) {
    onChange({ ...value, [`${metric}Min`]: String(next[0]), [`${metric}Max`]: String(next[1]) });
  }
  return <div className="range-target">
    <div className="range-value">
      <output dir="ltr">{selected ? `${quantity(locale, bounds[0], unit)} \u2013 ${quantity(locale, bounds[1], unit)}` : t(locale, 'notSpecified')}</output>
      <button type="button" className="icon-button" aria-label={t(locale, action)} title={t(locale, action)}
        onClick={() => selected ? onChange({ ...value, [`${metric}Min`]: '', [`${metric}Max`]: '' }) : changeBounds(defaults[metric])}>
        {selected ? <X size={16} /> : <Plus size={16} />}
      </button>
    </div>
    <Slider.Root className="range-slider" dir={locale === 'he' ? 'rtl' : 'ltr'} value={bounds}
      min={limits.min} max={limits.max} step={1} disabled={!selected} onValueChange={changeBounds}>
      <Slider.Track className="range-track"><Slider.Range className="range-selection" /></Slider.Track>
      <Slider.Thumb className="range-thumb" aria-disabled={!selected} aria-label={t(locale, 'minimum')} aria-valuetext={quantity(locale, bounds[0], unit)} />
      <Slider.Thumb className="range-thumb" aria-disabled={!selected} aria-label={t(locale, 'maximum')} aria-valuetext={quantity(locale, bounds[1], unit)} />
    </Slider.Root>
    <div className="range-scale" aria-hidden="true"><span>{quantity(locale, limits.min, unit)}</span><span>{quantity(locale, limits.max, unit)}</span></div>
  </div>;
}
