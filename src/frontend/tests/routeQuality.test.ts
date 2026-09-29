import { describe, expect, it } from 'vitest';
import { validRoadCandidates } from '../src/routeQuality';
import { candidates } from './fixtures';

export const quality = {
  policyVersion: 'road-v1', geometryLengthMeters: 10000, surfaceEvidenceState: 'partial', waytypeSupplied: false,
  surface: { pavedMeters: 5000, nonRoadMeters: 0, otherKnownMeters: 0, unknownMeters: 5000 },
  ways: { unknownMeters: 10000, stateRoadMeters: 0, roadMeters: 0, streetMeters: 0, pathMeters: 0, trackMeters: 0, cyclewayMeters: 0, footwayMeters: 0, stepsMeters: 0, ferryMeters: 0, constructionMeters: 0 },
  repeatedMeters: 200, sharedStemMeters: 100, remainingRepeatedMeters: 100,
};
const result = () => ({ ...structuredClone(candidates), excludedCandidates: [], candidates: [{ ...structuredClone(candidates.candidates[0]), assessment: { ...candidates.candidates[0].assessment!, quality: structuredClone(quality) } }] });
const empty = () => ({ ...result(), candidates: [], warnings: ['no_candidate_meets_requirements'], excludedCandidates: [{ seed: 2, distanceMeters: 40000, estimatedDurationSeconds: 3600, assessment: { ...result().candidates[0].assessment, targetsMatched: false }, reasons: ['targets_not_met'] }] });

describe('road quality response validation', () => {
  it('accepts three retained candidates and unavailable surface evidence', () => {
    const value = result();
    value.candidates = [1, 2, 3].map(seed => ({ ...value.candidates[0], seed }));
    expect(validRoadCandidates(value)).toBe(true);
    const q = value.candidates[0].assessment.quality;
    q.surfaceEvidenceState = 'unavailable';
    q.surface = { pavedMeters: 0, nonRoadMeters: 0, otherKnownMeters: 0, unknownMeters: 10000 };
    expect(validRoadCandidates(value)).toBe(true);
    q.surfaceEvidenceState = 'complete';
    expect(validRoadCandidates(value)).toBe(false);
  });
  it('retains the inclusive non-road surface threshold, not excess coverage', () => {
    const value = result(); const q = value.candidates[0].assessment.quality;
    q.surface.nonRoadMeters = 100; q.surface.pavedMeters -= 100;
    expect(validRoadCandidates(value)).toBe(true);
    q.surface.nonRoadMeters += .01; q.surface.pavedMeters -= .01;
    expect(validRoadCandidates(value)).toBe(false);
  });
  it.each([false, true])('rejects array-valued evidence states (excluded=%s)', excluded => {
    const value = excluded ? empty() : result();
    const item = excluded ? value.excludedCandidates[0] : value.candidates[0];
    (item.assessment.quality as Record<string, unknown>).surfaceEvidenceState = ['partial'];
    expect(validRoadCandidates(value)).toBe(false);
  });
  it('accepts both retained and explained empty results', () => { expect(validRoadCandidates(result())).toBe(true); expect(validRoadCandidates(empty())).toBe(true); });
  it.each(['policyVersion', 'surfaceEvidenceState', 'geometryLengthMeters', 'surface', 'ways', 'repeatedMeters'])('rejects missing %s', key => {
    const value = result(); delete (value.candidates[0].assessment.quality as Record<string, unknown>)[key];
    expect(validRoadCandidates(value)).toBe(false);
  });
  it.each([NaN, Infinity, -1])('rejects invalid numeric data %s', number => {
    const value = result(); value.candidates[0].assessment.quality.surface.pavedMeters = number; expect(validRoadCandidates(value)).toBe(false);
  });
  it('rejects contradictory totals and shared stem', () => {
    const value = result(); value.candidates[0].assessment.quality.surface.pavedMeters++;
    expect(validRoadCandidates(value)).toBe(false);
    const second = result(); second.candidates[0].assessment.quality.sharedStemMeters = 300;
    expect(validRoadCandidates(second)).toBe(false);
  });
  it('rejects malformed, duplicated and overlapping exclusions', () => {
    const value = empty(); value.excludedCandidates[0].reasons = ['made_up']; expect(validRoadCandidates(value)).toBe(false);
    const second = empty(); second.excludedCandidates.push(second.excludedCandidates[0]); expect(validRoadCandidates(second)).toBe(false);
    const third = { ...empty(), candidates: result().candidates }; third.excludedCandidates[0].seed = 1; expect(validRoadCandidates(third)).toBe(false);
  });
  it('does not treat absent data as no matches', () => {
    expect(validRoadCandidates({ ...empty(), excludedCandidates: [] })).toBe(false);
    expect(validRoadCandidates({ ...empty(), warnings: [] })).toBe(false);
    expect(validRoadCandidates(null)).toBe(false);
  });
  it('rejects too many results and retained target failures', () => {
    const value = result(); value.candidates = [1, 2, 3, 4].map(seed => ({ ...value.candidates[0], seed })); expect(validRoadCandidates(value)).toBe(false);
    const second = result(); second.candidates[0].assessment.targetsMatched = false; expect(validRoadCandidates(second)).toBe(false);
  });
});
