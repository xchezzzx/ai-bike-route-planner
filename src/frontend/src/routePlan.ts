import type { RoutePlan } from './types';

const failures = ['routing_not_configured', 'routing_credentials_rejected', 'unsupported_intent', 'route_not_found', 'routing_rate_limited', 'routing_unavailable', 'routing_timeout', 'routing_invalid_response', 'search_distance_out_of_range', 'routing_limit_exceeded'];

export function validPlan(plan: RoutePlan): boolean {
  if (!plan?.search || !Number.isInteger(plan.search.attemptedCount) || plan.search.attemptedCount < 1 || plan.search.attemptedCount > 3
    || !Number.isFinite(plan.search.requestedLengthMeters) || plan.search.requestedLengthMeters < 1000 || plan.search.requestedLengthMeters > 100000
    || ![0, 1].includes(plan.advisorCallCount)
    || !['notNeeded', 'skippedNoCandidates', 'skippedRoutingFailure', 'searched', 'stopped', 'failed'].includes(plan.advisorStatus)
    || !(plan.advisorFailure === null || ['notConfigured', 'authentication', 'quota', 'unavailable', 'timeout', 'invalidResponse'].includes(plan.advisorFailure))
    || !Array.isArray(plan.attempts) || plan.attempts.length !== plan.search.attemptedCount) return false;
  const used = new Set<number>();
  return plan.attempts.every(attempt => {
    if (!attempt || !Number.isInteger(attempt.seed) || attempt.seed < 1 || attempt.seed > 16 || used.has(attempt.seed)
      || !Number.isFinite(attempt.requestedLengthMeters) || attempt.requestedLengthMeters < 1000 || attempt.requestedLengthMeters > 100000
      || !['accepted', 'duplicate', 'noRoute', 'failed'].includes(attempt.outcome)
      || !['distance', 'duration', 'elevation', 'explore', 'stop'].includes(attempt.reason)
      || !(attempt.failure === null || failures.includes(attempt.failure))) return false;
    used.add(attempt.seed);
    return true;
  });
}
