import type { Plan, PlanRun } from '../api/planQueries';

/** The newest attempt when it failed closed and nothing published since: no later published attempt, no plan for that session or a later one. Otherwise null. */
export function unpublishedRun(runs: readonly PlanRun[], plans: readonly Pick<Plan, 'forDate'>[]): PlanRun | null {
  const newest = runs[0];
  if (newest?.outcome !== 'failed_closed' || newest.planId !== null) return null;
  const published = runs.some((r) => r.forDate >= newest.forDate && r.outcome === 'published');
  const planned = plans.some((p) => p.forDate >= newest.forDate);
  return published || planned ? null : newest;
}
