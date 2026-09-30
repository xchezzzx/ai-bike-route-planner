import type { Coordinate, Locale } from './types';
import { useEffect, useId, useMemo, useState } from 'react';
import { MapPin, X } from 'lucide-react';
import { nearestPlace, searchPlaces, type Place } from './places';
import { t } from './i18n';
interface Props { field: 'start' | 'destination'; locale: Locale; point?: Coordinate; onChange: () => void; onSelect: (point: Coordinate) => void }
export default function PlaceInput({ field, locale, point, onChange, onSelect }: Props) {
  const id = useId(); const [query, setQuery] = useState(''); const [open, setOpen] = useState(false); const [active, setActive] = useState(-1);
  const nearest = useMemo(() => point ? nearestPlace(point) : undefined, [point?.latitude, point?.longitude]);
  const options = useMemo(() => searchPlaces(query), [query]);
  const text = (key: Parameters<typeof t>[1]) => t(locale, key);
  const label = point ? nearest ? `${text('nearPlace')} ${nearest.name}` : text('mapPoint') : query;
  useEffect(() => { if (point) { setQuery(''); setOpen(false); setActive(-1); } }, [point?.latitude, point?.longitude]);
  useEffect(() => {
    if (open && active >= 0) document.getElementById(`${id}-option-${active}`)?.scrollIntoView?.({ block: 'nearest' });
  }, [open, active, id]);
  function select(place: Place) { setQuery(''); setOpen(false); setActive(-1); onSelect({ latitude: place.latitude, longitude: place.longitude }); }
  return <div className="place-input" onBlur={event => { if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false); }}>
    <label htmlFor={id}><span><span className={`point-dot ${field}`} />{text(field)}</span></label>
    <div className="place-row"><MapPin size={16} aria-hidden="true" />
      <input id={id} role="combobox" dir="auto" autoComplete="off" maxLength={200} value={label} placeholder={text('findSettlement')}
        aria-autocomplete="list" aria-expanded={open} aria-controls={`${id}-options`} aria-activedescendant={open && active >= 0 && options[active] ? `${id}-option-${active}` : undefined}
        onFocus={event => { if (point) event.target.select(); else if (query.length >= 2) setOpen(true); }}
        onChange={event => { setQuery(event.target.value); setOpen(event.target.value.trim().length >= 2); setActive(-1); onChange(); }}
        onKeyDown={event => {
          if (event.key === 'Escape') { setOpen(false); setActive(-1); }
          if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault(); setOpen(true);
            setActive(index => options.length ? event.key === 'ArrowDown' ? (index + 1) % options.length : (index <= 0 ? options.length - 1 : index - 1) : -1);
          }
          if (event.key === 'Enter' && open && active >= 0 && options[active]) { event.preventDefault(); select(options[active]); }
        }} />
      <button className="icon-button" type="button" title={text(field === 'start' ? 'clearStart' : 'clearDestination')} aria-label={text(field === 'start' ? 'clearStart' : 'clearDestination')}
        onClick={() => { setQuery(''); setOpen(false); setActive(-1); onChange(); }}><X size={16} /></button>
    </div>
    {open && <div id={`${id}-options`} className="place-options" role="listbox" aria-label={text(field)}>
      {options.map((place, index) => <div key={place.id} id={`${id}-option-${index}`} role="option" aria-selected={active === index}
        onMouseDown={event => event.preventDefault()} onClick={() => select(place)}><span dir="auto">{place.name}</span><small dir="ltr">{place.latitude.toFixed(4)}, {place.longitude.toFixed(4)}</small></div>)}
      {!options.length && <p role="status">{text('noSettlements')}</p>}
    </div>}
  </div>;
}
