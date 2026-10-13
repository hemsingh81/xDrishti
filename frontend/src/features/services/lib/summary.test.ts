import {
  buildAccountSyncService,
  buildFailedRun,
  buildMasterService,
  buildServiceRun,
  buildServiceView,
  serviceIds,
} from '@/test/servicesFixtures';
import { summarise } from './summary';

describe('summarise', () => {
  it('counts the services by status', () => {
    const summary = summarise([
      buildServiceView(),
      buildMasterService({ status: 'running' }),
      buildServiceView({ id: 'a', status: 'paused', enabled: false }),
      buildServiceView({ id: 'b', status: 'failed' }),
      buildServiceView({ id: 'c', status: 'failed' }),
    ]);

    expect(summary).toMatchObject({ total: 5, healthy: 1, running: 1, paused: 1, failed: 2 });
  });

  it('finds the service that starts first among the enabled ones', () => {
    const summary = summarise([
      buildMasterService({ nextRun: '2026-10-13T18:30:00+05:30' }),
      buildServiceView({ nextRun: '2026-10-13T15:50:00+05:30' }),
      buildServiceView({ id: 'paused', enabled: false, nextRun: '2026-10-13T09:00:00+05:30' }),
      buildServiceView({ id: 'none', nextRun: null }),
    ]);

    expect(summary.next?.service.id).toBe(serviceIds.eod);
    expect(summary.next?.at).toBe('2026-10-13T15:50:00+05:30');
  });

  it('compares moments, not strings (offsets differ)', () => {
    const summary = summarise([
      buildServiceView({ id: 'a', nextRun: '2026-10-13T10:00:00Z' }),
      buildServiceView({ id: 'b', nextRun: '2026-10-13T15:00:00+05:30' }),
    ]);

    // 15:00 in India is 09:30 UTC, before 10:00 UTC.
    expect(summary.next?.service.id).toBe('b');
  });

  it('finds the run that ended last, or started last while it still runs', () => {
    const summary = summarise([
      buildServiceView({ lastRun: buildFailedRun() }),
      buildMasterService(),
      buildAccountSyncService({
        lastRun: buildServiceRun({ startedAt: '2026-10-13T09:00:00+05:30', finishedAt: null, status: 'running' }),
      }),
    ]);

    expect(summary.last?.service.id).toBe(serviceIds.sync);
    expect(summary.last?.at).toBe('2026-10-13T09:00:00+05:30');
  });

  it('has nothing to say about the next and last run when there are none', () => {
    const summary = summarise([buildServiceView({ nextRun: null, lastRun: null })]);

    expect(summary.next).toBeNull();
    expect(summary.last).toBeNull();
    expect(summarise([])).toMatchObject({ total: 0, healthy: 0, next: null, last: null });
  });
});
