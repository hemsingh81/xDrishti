import type { ServiceView } from '@/shared/api';
import type { GridColumn } from '@/shared/ui/grid';
import { LastRun } from './LastRun';
import { NextRun } from './NextRun';
import { ScheduleSummary } from './ScheduleSummary';
import { ServiceActions, type ServiceActionHandlers } from './ServiceActions';
import { ServiceName } from './ServiceName';
import { ServiceStatus } from './ServiceStatus';

/**
 * The columns of the services table: service and what it does, schedule in words, runs as (from `lg`), status, last
 * run, next run (India time) and the actions. Build once per set of handlers (rows are memoised).
 */
export function serviceColumns(
  handlers: ServiceActionHandlers,
  names: ReadonlyMap<string, string>,
  busyId: string | null,
): GridColumn<ServiceView>[] {
  return [
    { id: 'service', header: 'Service', minWidth: 240, grow: true, render: (s) => <ServiceName service={s} /> },
    { id: 'schedule', header: 'Schedule', minWidth: 240, render: (s) => <ScheduleSummary service={s} /> },
    {
      id: 'runsAs',
      header: 'Runs as',
      hideBelow: 'lg',
      render: (s) => <span className="mono">{s.runsAs}</span>,
    },
    { id: 'status', header: 'Status', minWidth: 150, render: (s) => <ServiceStatus service={s} /> },
    { id: 'lastRun', header: 'Last run', minWidth: 200, render: (s) => <LastRun run={s.lastRun} /> },
    { id: 'nextRun', header: 'Next run (IST)', minWidth: 130, render: (s) => <NextRun service={s} names={names} /> },
    {
      id: 'actions',
      header: 'Actions',
      width: 230,
      align: 'right',
      render: (s) => <ServiceActions service={s} busy={busyId === s.id} {...handlers} />,
    },
  ];
}
