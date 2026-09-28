import { useEffect, useRef, useState } from 'react';
import * as maplibregl from 'maplibre-gl';
import type { GeoJSONSource, Map as LibreMap } from 'maplibre-gl';
import workerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url';
import type { FeatureCollection, LineString } from 'geojson';
import { LocateFixed, Maximize, Minus, Plus, RefreshCw } from 'lucide-react';
import { t } from './i18n';
import type { Candidate, Coordinate, Locale } from './types';

interface Props {
  locale: Locale;
  start?: Coordinate;
  destination?: Coordinate;
  pick: 'start' | 'destination';
  candidates: Candidate[];
  selected: number;
  onRouteSelect: (index: number) => void;
  onSelect: (coordinate: Coordinate) => void;
}
const colors = ['#15724f', '#ba4661', '#346db5'];
const center: [number, number] = [34.79, 32.085];
// Bundle the worker and its shared module together for both Vite dev and production.
maplibregl.setWorkerUrl(workerUrl);

function fit(map: LibreMap, candidate?: Candidate) {
  if (!candidate) return;
  const bounds = new maplibregl.LngLatBounds();
  candidate.route.geometry.forEach(point => bounds.extend([point.longitude, point.latitude]));
  map.fitBounds(bounds, { padding: 52, maxZoom: 15, duration: 0 });
}

export default function RouteMap(props: Props) {
  const container = useRef<HTMLDivElement>(null);
  const mapRef = useRef<LibreMap | null>(null);
  const latest = useRef(props);
  latest.current = props;
  const [ready, setReady] = useState(false);
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const text = (key: Parameters<typeof t>[1]) => t(props.locale, key);

  useEffect(() => {
    let map: LibreMap | undefined;
    let resize: ResizeObserver | undefined;
    let disposed = false;
    setReady(false); setFailed(false);
    const deadline = setTimeout(() => { if (!disposed) setFailed(true); }, 20000);
    try {
      map = new maplibregl.Map({
        container: container.current!,
        style: 'https://tiles.openfreemap.org/styles/liberty',
        center, zoom: 11, attributionControl: false,
        canvasContextAttributes: { preserveDrawingBuffer: true },
      });
      mapRef.current = map;
      map.getCanvas().setAttribute('aria-label', t(latest.current.locale, 'mapCanvas'));
      map.addControl(new maplibregl.AttributionControl({ compact: false }), 'bottom-right');
      map.on('error', () => { if (!disposed) { clearTimeout(deadline); setFailed(true); } });
      map.on('webglcontextlost', () => { if (!disposed) setFailed(true); });
      map.on('load', () => {
        if (disposed) return;
        clearTimeout(deadline); setReady(true); setFailed(false);
      });
      map.on('click', event => {
        const features = map!.getLayer('route-lines') ? map!.queryRenderedFeatures(event.point, { layers: ['route-lines', 'selected-route'] }) : [];
        if (features.length) {
          const index = Number(features[0].properties.index);
          if (Number.isInteger(index) && latest.current.candidates[index]) latest.current.onRouteSelect(index);
        } else latest.current.onSelect({ latitude: event.lngLat.lat, longitude: event.lngLat.wrap().lng });
      });
      resize = new ResizeObserver(() => { map?.resize(); });
      resize.observe(container.current!);
    } catch { clearTimeout(deadline); setFailed(true); }
    return () => {
      disposed = true; clearTimeout(deadline); resize?.disconnect(); map?.remove(); mapRef.current = null;
    };
  }, [attempt]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    map.getCanvas().setAttribute('aria-label', text('mapCanvas'));
    if (!ready) return;
    const markers: maplibregl.Marker[] = [];
    for (const key of ['start', 'destination'] as const) {
      const point = props[key];
      if (!point) continue;
      const element = document.createElement('div');
      element.className = `map-point ${key}`;
      element.setAttribute('role', 'img'); element.setAttribute('aria-label', text(key));
      markers.push(new maplibregl.Marker({ element }).setLngLat([point.longitude, point.latitude]).addTo(map));
    }
    return () => markers.forEach(marker => marker.remove());
  }, [ready, props.start?.latitude, props.start?.longitude, props.destination?.latitude, props.destination?.longitude, props.locale]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map || !ready) return;
    const data: FeatureCollection<LineString> = { type: 'FeatureCollection', features: props.candidates.map((candidate, index) => ({
      type: 'Feature', properties: { index, color: colors[index % colors.length] },
      geometry: { type: 'LineString', coordinates: candidate.route.geometry.map(point => [point.longitude, point.latitude]) },
    })) };
    let disposed = false;
    try {
      if (map.getSource('routes')) void (map.getSource('routes') as GeoJSONSource).setData(data).catch(() => { if (!disposed) setFailed(true); });
      else {
        map.addSource('routes', { type: 'geojson', data });
        map.addLayer({ id: 'route-lines', type: 'line', source: 'routes', layout: { 'line-cap': 'round', 'line-join': 'round' }, paint: { 'line-color': ['get', 'color'], 'line-width': 4, 'line-opacity': 0.55 } });
        map.addLayer({ id: 'selected-outline', type: 'line', source: 'routes', layout: { 'line-cap': 'round', 'line-join': 'round' }, paint: { 'line-color': '#ffffff', 'line-width': 9 } });
        map.addLayer({ id: 'selected-route', type: 'line', source: 'routes', layout: { 'line-cap': 'round', 'line-join': 'round' }, paint: { 'line-color': ['get', 'color'], 'line-width': 5 } });
      }
      map.setFilter('selected-outline', ['==', ['get', 'index'], props.selected]);
      map.setFilter('selected-route', ['==', ['get', 'index'], props.selected]);
      fit(map, props.candidates[props.selected]);
    } catch { setFailed(true); }
    return () => { disposed = true; };
  }, [ready, props.candidates, props.selected]);

  return <section className="map-panel" aria-label={text('map')} aria-busy={!ready && !failed}>
    <div ref={container} className="map-container" />
    {(!ready || failed) && <div className="map-status" role="status"><p>{text(failed ? 'mapError' : 'mapLoading')}</p>{failed && <button type="button" onClick={() => setAttempt(value => value + 1)}><RefreshCw size={14} />{text('retryMap')}</button>}</div>}
    <div className="map-toolbar">
      <button className="icon-button" type="button" title={text('zoomIn')} aria-label={text('zoomIn')} disabled={!ready} onClick={() => mapRef.current?.zoomIn({ duration: 0 })}><Plus size={19} /></button>
      <button className="icon-button" type="button" title={text('zoomOut')} aria-label={text('zoomOut')} disabled={!ready} onClick={() => mapRef.current?.zoomOut({ duration: 0 })}><Minus size={19} /></button>
      <button className="icon-button" type="button" title={text('fit')} aria-label={text('fit')} disabled={!ready || !props.candidates.length} onClick={() => mapRef.current && fit(mapRef.current, props.candidates[props.selected])}><Maximize size={17} /></button>
      <button className="icon-button" type="button" title={text('resetMap')} aria-label={text('resetMap')} disabled={!ready} onClick={() => mapRef.current?.jumpTo({ center, zoom: 11 })}><LocateFixed size={18} /></button>
    </div>
  </section>;
}
