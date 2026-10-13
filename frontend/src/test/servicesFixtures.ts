import type {
  ScheduleSpec,
  ServiceIssue,
  ServiceNotice,
  ServiceParameter,
  ServiceRun,
  ServicesTimeline,
  ServiceView,
  TimelineEntry,
} from '@/shared/api';

/** Ids of the sample services: the end-of-day run and the instrument refresh are registered, the account sync is read-only. */
export const serviceIds = { eod: 'eod', master: 'instrument-master', sync: 'account-sync' } as const;

export const serviceRunId = (n: number) => `00000000-0000-4000-8000-0000000009${String(n).padStart(2, '0')}`;

export const weekdaySet = ['mon', 'tue', 'wed', 'thu', 'fri'];

export const buildScheduleSpec = (overrides: Partial<ScheduleSpec> = {}): ScheduleSpec => ({
  type: 'daily',
  time: '15:50',
  days: weekdaySet,
  tradingDaysOnly: true,
  ...overrides,
});

/** A finished scheduled run of the end-of-day service. */
export const buildServiceRun = (overrides: Partial<ServiceRun> = {}): ServiceRun => ({
  id: serviceRunId(1),
  serviceId: serviceIds.eod,
  trigger: 'scheduled',
  attempt: 1,
  startedAt: '2026-10-12T15:50:00+05:30',
  finishedAt: '2026-10-12T15:58:00+05:30',
  status: 'succeeded',
  message: null,
  dataJobId: null,
  ...overrides,
});

export const buildFailedRun = (overrides: Partial<ServiceRun> = {}): ServiceRun =>
  buildServiceRun({
    id: serviceRunId(2),
    status: 'failed',
    startedAt: '2026-10-12T15:50:00+05:30',
    finishedAt: '2026-10-12T15:51:00+05:30',
    message: 'Dhan refused the access token.',
    ...overrides,
  });

/** `count` runs, newest first, one a day back from 12 Oct, alternating the trigger. */
export const buildRunHistory = (serviceId: string, count: number): ServiceRun[] =>
  Array.from({ length: count }, (_, index) => {
    const day = String(12 - (index % 12)).padStart(2, '0');
    return buildServiceRun({
      id: serviceRunId(10 + index),
      serviceId,
      trigger: index % 2 === 0 ? 'scheduled' : 'manual',
      startedAt: `2026-10-${day}T15:50:00+05:30`,
      finishedAt: `2026-10-${day}T15:5${String(index % 10)}:00+05:30`,
      message: `Run ${String(index + 1)}`,
    });
  });

export const buildServiceParameter = (overrides: Partial<ServiceParameter> = {}): ServiceParameter => ({
  name: 'lookbackDays',
  label: 'Gap look-back',
  kind: 'integer',
  min: 1,
  max: 30,
  value: 5,
  default: 5,
  description: 'Sessions looked at again for missing bars',
  unit: 'days',
  ...overrides,
});

export const buildServiceIssue = (overrides: Partial<ServiceIssue> = {}): ServiceIssue => ({
  serviceId: serviceIds.eod,
  field: 'schedule.time',
  severity: 'warning',
  code: 'services.eod_too_early',
  message: 'The end-of-day fetch before 15:35 may miss the last bars of the session.',
  relatedServiceId: null,
  ...overrides,
});

/** A service as the list shows it (the end-of-day run unless overridden): healthy, daily on trading days. */
export const buildServiceView = (overrides: Partial<ServiceView> = {}): ServiceView => ({
  id: serviceIds.eod,
  name: 'End-of-day data run',
  description: 'Loads the day from Dhan, builds every timeframe and checks the data.',
  runsAs: 'service identity',
  readOnly: false,
  manageUrl: null,
  enabled: true,
  status: 'healthy',
  schedule: buildScheduleSpec(),
  scheduleWords: 'Every trading day at 15:50 IST',
  allowedScheduleTypes: ['daily', 'weekly', 'after'],
  execution: { retries: 2, timeoutMinutes: 60, catchUp: true, notifyOnFailure: true },
  parameters: [
    buildServiceParameter(),
    buildServiceParameter({
      name: 'fetchGaps',
      label: 'Fetch gaps again',
      kind: 'boolean',
      min: 0,
      max: 0,
      value: true,
      default: true,
      description: 'Ask Dhan again for sessions that were left incomplete',
      unit: null,
    }),
  ],
  lastRun: buildServiceRun(),
  nextRun: '2026-10-13T15:50:00+05:30',
  issues: [],
  isDefault: true,
  revision: 3,
  ...overrides,
});

export const buildMasterService = (overrides: Partial<ServiceView> = {}): ServiceView =>
  buildServiceView({
    id: serviceIds.master,
    name: 'Instrument master refresh',
    description: 'Reads the broker’s list of instruments and keeps the master up to date.',
    schedule: buildScheduleSpec({
      time: '18:30',
      tradingDaysOnly: false,
      days: ['mon', 'tue', 'wed', 'thu', 'fri', 'sat', 'sun'],
    }),
    scheduleWords: 'Daily at 18:30 IST',
    allowedScheduleTypes: ['daily', 'weekly', 'after', 'interval', 'window', 'continuous'],
    execution: { retries: 1, timeoutMinutes: 30, catchUp: true, notifyOnFailure: false },
    parameters: [
      buildServiceParameter({
        name: 'minTurnover',
        label: 'Minimum turnover',
        kind: 'number',
        min: 0,
        max: 100,
        value: 2.5,
        default: 2.5,
        description: null,
        unit: 'crore',
      }),
    ],
    lastRun: buildServiceRun({
      id: serviceRunId(3),
      serviceId: serviceIds.master,
      startedAt: '2026-10-12T18:30:00+05:30',
      finishedAt: '2026-10-12T18:31:00+05:30',
    }),
    nextRun: '2026-10-13T18:30:00+05:30',
    revision: 1,
    ...overrides,
  });

/** The per-account holdings sync: set in Accounts, shown read-only with a link. */
export const buildAccountSyncService = (overrides: Partial<ServiceView> = {}): ServiceView =>
  buildServiceView({
    id: serviceIds.sync,
    name: 'Account & portfolio sync',
    description: 'Refreshes the holdings of each account once a day — set per account.',
    runsAs: 'your session',
    readOnly: true,
    manageUrl: '/accounts',
    schedule: buildScheduleSpec({ time: '16:00', tradingDaysOnly: false }),
    scheduleWords: 'Once a day per account, as set in Accounts',
    allowedScheduleTypes: [],
    execution: { retries: 0, timeoutMinutes: 10, catchUp: false, notifyOnFailure: false },
    parameters: [],
    lastRun: null,
    nextRun: '2026-10-13T16:00:00+05:30',
    revision: 0,
    ...overrides,
  });

export const sampleServices = (): ServiceView[] => [
  buildServiceView(),
  buildMasterService(),
  buildAccountSyncService(),
];

export const buildTimelineEntry = (overrides: Partial<TimelineEntry> = {}): TimelineEntry => ({
  serviceId: serviceIds.eod,
  name: 'End-of-day data run',
  kind: 'mark',
  marks: [{ time: '15:50', at: '2026-10-13T15:50:00+05:30' }],
  band: null,
  everyMinutes: null,
  followsServiceId: null,
  ...overrides,
});

export const buildTimeline = (overrides: Partial<ServicesTimeline> = {}): ServicesTimeline => ({
  date: '2026-10-13',
  tradingDay: true,
  marketOpen: '09:15',
  marketClose: '15:30',
  reviewCutoff: '16:30',
  entries: [
    buildTimelineEntry(),
    buildTimelineEntry({
      serviceId: serviceIds.master,
      name: 'Instrument master refresh',
      marks: [{ time: '18:30', at: '2026-10-13T18:30:00+05:30' }],
    }),
  ],
  ...overrides,
});

export const buildServiceNotice = (overrides: Partial<ServiceNotice> = {}): ServiceNotice => ({
  id: `service:failed:${serviceRunId(2)}`,
  kind: 'service_failed',
  severity: 'error',
  text: 'End-of-day data run failed: Dhan refused the access token.',
  serviceId: serviceIds.eod,
  at: '2026-10-12T15:51:00+05:30',
  ...overrides,
});
