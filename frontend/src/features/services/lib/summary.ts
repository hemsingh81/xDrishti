import type { ServiceRun, ServiceView } from '@/shared/api';

export interface ServicesSummary {
  total: number;
  healthy: number;
  running: number;
  paused: number;
  failed: number;
  /** The enabled service that starts first, and when. */
  next: { service: ServiceView; at: string } | null;
  /** The service whose latest run ended (started, while it runs) most recently. */
  last: { service: ServiceView; run: ServiceRun; at: string } | null;
}

const moment = (run: ServiceRun) => run.finishedAt ?? run.startedAt;

/** Counts by status, the next start of all and the latest run of all, for the strip above the cards. */
export function summarise(services: readonly ServiceView[]): ServicesSummary {
  const count = (status: string) => services.filter((s) => s.status === status).length;
  let next: ServicesSummary['next'] = null;
  let last: ServicesSummary['last'] = null;
  for (const service of services) {
    if (service.enabled && service.nextRun && (next === null || Date.parse(service.nextRun) < Date.parse(next.at))) {
      next = { service, at: service.nextRun };
    }
    const run = service.lastRun;
    if (run && (last === null || Date.parse(moment(run)) > Date.parse(last.at))) {
      last = { service, run, at: moment(run) };
    }
  }
  return {
    total: services.length,
    healthy: count('healthy'),
    running: count('running'),
    paused: count('paused'),
    failed: count('failed'),
    next,
    last,
  };
}
