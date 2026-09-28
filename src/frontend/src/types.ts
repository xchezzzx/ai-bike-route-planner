export type Locale = 'en' | 'ru' | 'he';
export interface Coordinate { latitude: number; longitude: number }
export interface CoordinateInput { latitude: string; longitude: string }
export interface Intent {
  start: Coordinate;
  destination?: Coordinate | null;
  shape: 'loop' | 'pointToPoint';
  profile: 'road' | 'gravel';
  elevation: 'balanced' | 'minimize' | 'seekClimbs';
  targetDistanceMeters?: number | null;
  targetDurationSeconds?: number | null;
}
export type Draft = Partial<Intent>;
export interface Interpretation {
  status: 'ready' | 'needsClarification' | 'unsupported';
  draft: Draft;
  intent: Intent | null;
  clarifications: { field: string; code: string; message: string }[];
  limitations: string[];
  assumptions: string[];
}
export interface GeneratedRoute {
  geometry: (Coordinate & { elevationMeters: number | null })[];
  distanceMeters: number;
  estimatedDurationSeconds: number;
  ascentMeters: number | null;
  descentMeters: number | null;
  attribution: string;
  warnings: string[];
  gpx: string;
}
export interface Candidate {
  seed: number;
  assessment: { distanceDeltaMeters: number | null; durationDeltaSeconds: number | null; targetsMatched: boolean; score: number } | null;
  route: GeneratedRoute;
}
export interface Candidates {
  requestedLengthMeters: number;
  assumptions: string[];
  attemptedCount: number;
  warnings: string[];
  candidates: Candidate[];
}
export interface ManualInput { shape: string; profile: string; elevation: string; distance: string; duration: string }
export interface Inputs {
  locale: Locale;
  mode: 'prompt' | 'manual';
  prompt: string;
  start: CoordinateInput;
  destination: CoordinateInput;
  manual: ManualInput;
}
