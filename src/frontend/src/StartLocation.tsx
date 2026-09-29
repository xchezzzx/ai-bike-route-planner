import { Check, LoaderCircle, X } from 'lucide-react';
import { quantity, t } from './i18n';
import type { Locale } from './types';
import type { useStartLocation } from './useStartLocation';

export default function StartLocation({ locale, location }: { locale: Locale; location: ReturnType<typeof useStartLocation> }) {
  const state = location.state;
  if (state.status === 'idle') return null;
  return <div className="location-status" role={state.status === 'error' ? 'alert' : 'status'}>
    {state.status === 'locating' && <><LoaderCircle size={16} className="spinner" /><span>{t(locale, 'locating')}</span></>}
    {state.status === 'error' && <span>{t(locale, state.code)}</span>}
    {(state.status === 'confirm' || state.status === 'applied') && <span>
      {t(locale, state.status === 'confirm' ? 'locationCoarse' : 'locationApplied')}
      {' '}{t(locale, 'locationAccuracy')}: <bdi>{quantity(locale, state.accuracy, 'm')}</bdi>
      {state.status === 'confirm' && <span className="location-point" dir="ltr">{state.point.latitude.toFixed(6)}, {state.point.longitude.toFixed(6)}</span>}
    </span>}
    {state.status === 'confirm' && <button type="button" className="icon-button" title={t(locale, 'confirmLocation')} aria-label={t(locale, 'confirmLocation')} onClick={location.confirm}><Check size={18} /></button>}
    {(state.status === 'confirm' || state.status === 'locating') && <button type="button" className="icon-button" title={t(locale, 'cancel')} aria-label={t(locale, 'cancel')} onClick={location.cancel}><X size={18} /></button>}
  </div>;
}
