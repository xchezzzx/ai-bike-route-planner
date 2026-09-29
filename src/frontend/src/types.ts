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
	segments?: RouteSegment[] | null;
  geometry: (Coordinate & { elevationMeters: number | null })[];
  distanceMeters: number;
  estimatedDurationSeconds: number;
  ascentMeters: number | null;
  descentMeters: number | null;
  attribution: string;
  warnings: string[];
  gpx: string;
}
export type RouteSurface = 'asphalt' | 'paved' | 'unpaved' | 'other' | 'unknown';
export type RouteWayType = 'stateRoad' | 'road' | 'street' | 'path' | 'track' | 'cycleway' | 'footway' | 'steps' | 'ferry' | 'construction' | 'unknown';
export interface RouteSegment { fromPointIndex: number; toPointIndex: number; surface: RouteSurface; wayType: RouteWayType }
export interface Candidate {
  seed: number;
  assessment: CandidateAssessment | null;
  route: GeneratedRoute;
}
export interface CandidateAssessment {
  distanceDeltaMeters: number | null; durationDeltaSeconds: number | null; targetsMatched: boolean; score: number;
  quality: RoadQuality;
}
export interface RoadQuality {
  policyVersion: 'road-v1'; geometryLengthMeters: number;
  surfaceEvidenceState: 'unavailable' | 'partial' | 'complete'; waytypeSupplied: boolean;
  surface: { pavedMeters: number; nonRoadMeters: number; otherKnownMeters: number; unknownMeters: number };
  ways: { unknownMeters: number; stateRoadMeters: number; roadMeters: number; streetMeters: number; pathMeters: number; trackMeters: number; cyclewayMeters: number; footwayMeters: number; stepsMeters: number; ferryMeters: number; constructionMeters: number };
  repeatedMeters: number; sharedStemMeters: number; remainingRepeatedMeters: number;
}
export interface ExcludedCandidate {
  seed: number; distanceMeters: number; estimatedDurationSeconds: number; assessment: CandidateAssessment; reasons: string[];
}
export interface Candidates {
  requestedLengthMeters: number;
  assumptions: string[];
  attemptedCount: number;
  warnings: string[];
  candidates: Candidate[];
  excludedCandidates: ExcludedCandidate[];
}
export interface RoutePlan {
  search: Candidates;
  advisorCallCount: number;
  advisorStatus: 'notNeeded' | 'skippedNoCandidates' | 'skippedRoutingFailure' | 'searched' | 'stopped' | 'failed';
  advisorFailure: 'notConfigured' | 'authentication' | 'quota' | 'unavailable' | 'timeout' | 'invalidResponse' | null;
  attempts: { seed: number; requestedLengthMeters: number; outcome: 'accepted' | 'duplicate' | 'noRoute' | 'failed'; reason: 'distance' | 'duration' | 'elevation' | 'explore' | 'stop'; failure: string | null }[];
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
