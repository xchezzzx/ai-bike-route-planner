import type { Candidates, GeneratedRoute, Intent, Interpretation } from '../src/types';

export const intent: Intent = {
  start: { latitude: 32.08, longitude: 34.78 }, destination: null,
  shape: 'loop', profile: 'road', elevation: 'balanced', targetDistanceMeters: 25000, targetDurationSeconds: null,
};
export const interpretation: Interpretation = {
  status: 'ready', draft: intent, intent,
  clarifications: [], limitations: [], assumptions: ['elevation_balanced'],
};
export const route: GeneratedRoute = {
  geometry: [
    { latitude: 32.08, longitude: 34.78, elevationMeters: null },
    { latitude: 32.11, longitude: 34.80, elevationMeters: null },
    { latitude: 32.09, longitude: 34.83, elevationMeters: null },
    { latitude: 32.08, longitude: 34.78, elevationMeters: null },
  ],
  distanceMeters: 24500, estimatedDurationSeconds: 4500, ascentMeters: null, descentMeters: null,
  attribution: 'openrouteservice | OpenStreetMap contributors', warnings: ['elevation_data_unavailable'],
  gpx: '<?xml version="1.0" encoding="UTF-8"?><gpx><trk><name>Test route 1</name></trk></gpx>',
};
export const candidates: Candidates = {
  requestedLengthMeters: 25000, assumptions: ['initial_speed_20_kmh'], attemptedCount: 3, warnings: ['candidate_search_limited'],
  candidates: [
    { seed: 1, assessment: { distanceDeltaMeters: -500, durationDeltaSeconds: null, targetsMatched: true, score: 0.02 }, route },
    { seed: 2, assessment: { distanceDeltaMeters: 2500, durationDeltaSeconds: null, targetsMatched: false, score: 0.1 }, route: { ...route, distanceMeters: 27500, ascentMeters: 120, descentMeters: 110, geometry: [route.geometry[0], { latitude: 32.06, longitude: 34.82, elevationMeters: 20 }, { latitude: 32.04, longitude: 34.79, elevationMeters: 30 }, route.geometry[0]], warnings: ['targets_not_met'], gpx: '<?xml version="1.0" encoding="UTF-8"?><gpx><trk><name>Test route 2 שלום</name></trk></gpx>' } },
  ],
};

export const refinement = {
  search: candidates, advisorCallCount: 1, advisorStatus: 'searched', advisorFailure: null,
  attempts: [
    { seed: 1, requestedLengthMeters: 25000, outcome: 'accepted', reason: 'explore', failure: null },
    { seed: 2, requestedLengthMeters: 25000, outcome: 'accepted', reason: 'explore', failure: null },
    { seed: 7, requestedLengthMeters: 22000, outcome: 'duplicate', reason: 'distance', failure: null },
  ],
};
