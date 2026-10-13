import { http, HttpResponse, type RequestHandler } from 'msw';
import type {
  ScheduleSpec,
  ServiceIssue,
  ServiceNotice,
  ServiceRun,
  ServiceScheduleInput,
  ServicesTimeline,
  ServiceView,
  TimelineEntry,
} from '@/shared/api';
import { buildDataJob, buildDhanPhase, buildRunningJob, dataJobId } from './fixtures';
import { problemResponse } from './server';
import {
  buildServiceIssue,
  buildTimeline,
  sampleServices,
  serviceIds,
  serviceRunId,
  weekdaySet,
} from './servicesFixtures';

export interface ServicesFakeOptions {
  /** The services of the list (default: the end-of-day run, the instrument refresh, the read-only account sync). */
  services?: readonly ServiceView[];
  /** Past runs by service id, newest first (default: the service's `lastRun` only). */
  runs?: Readonly<Record<string, readonly ServiceRun[]>>;
  /** Parts of the timeline to override (default: derived from the enabled services). */
  timeline?: Partial<ServicesTimeline>;
  /** The notices of the bell (default: derived from the failed runs of services that notify on failure). */
  notices?: readonly ServiceNotice[];
  /** How the next Run now ends (default succeeded). */
  runOutcome?: 'succeeded' | 'failed' | 'skipped';
  /** The message of a run that succeeded (default none). */
  runMessage?: string;
  /** A started run ends by itself the second time the list is read (default). Off: only `fake.advance()` ends it. */
  autoAdvance?: boolean;
  /** More findings for a draft (the dependent-service checks the fake does not know). */
  extraIssues?: (input: ServiceScheduleInput, service: ServiceView) => ServiceIssue[];
}

/** Reads that can be made to fail: set a flag, then clear it to let a retry succeed. */
export interface ServicesFailures {
  list: boolean;
  timeline: boolean;
  runs: boolean;
  preview: boolean;
}

const at = '2026-10-13T10:00:00+05:30';
const nextDay = (offset: number) => `2026-10-${String(13 + offset).padStart(2, '0')}`;
const dayNames = ['sun', 'mon', 'tue', 'wed', 'thu', 'fri', 'sat'] as const;
const weekdayOf = (iso: string) => dayNames[new Date(`${iso}T12:00:00+05:30`).getDay()] ?? 'mon';
const minutesOf = (time: string) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5));
const hm = (minutes: number) =>
  `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;

/** The next three starts of a schedule from 13 Oct (the fake's now): daily/weekly by day, window/interval from the start. */
function nextStarts(spec: ScheduleSpec): string[] {
  const days = spec.tradingDaysOnly ? weekdaySet : (spec.days ?? []);
  if (spec.type === 'daily' || spec.type === 'weekly') {
    const matches = Array.from({ length: 10 }, (_, i) => nextDay(i)).filter((day) =>
      spec.type === 'weekly' ? weekdayOf(day) === spec.weekday : days.includes(weekdayOf(day)),
    );
    return matches.slice(0, 3).map((day) => `${day}T${spec.time ?? '00:00'}:00+05:30`);
  }
  if (spec.type === 'interval' && spec.from && spec.everyMinutes) {
    const start = minutesOf(spec.from);
    return [0, 1, 2].map((i) => `2026-10-13T${hm(start + i * spec.everyMinutes!)}:00+05:30`);
  }
  if (spec.type === 'window' && spec.from) return [`2026-10-13T${spec.from}:00+05:30`];
  return [];
}

const weekdayNames: Record<string, string> = {
  mon: 'Monday',
  tue: 'Tuesday',
  wed: 'Wednesday',
  thu: 'Thursday',
  fri: 'Friday',
  sat: 'Saturday',
  sun: 'Sunday',
};

function wordsOf(spec: ScheduleSpec, names: ReadonlyMap<string, string>): string {
  switch (spec.type) {
    case 'daily':
      return `${spec.tradingDaysOnly ? 'Every trading day' : 'Daily'} at ${spec.time ?? ''} IST`;
    case 'weekly':
      return `Every ${weekdayNames[spec.weekday ?? 'mon'] ?? ''} at ${spec.time ?? ''} IST`;
    case 'after':
      return `After ${names.get(spec.after ?? '') ?? 'another service'} finishes`;
    case 'window':
      return `Up from ${spec.from ?? ''} to ${spec.to ?? ''} IST`;
    case 'interval':
      return `Every ${String(spec.everyMinutes ?? 0)} minutes from ${spec.from ?? ''} to ${spec.to ?? ''} IST`;
    default:
      return 'Always on';
  }
}

const statusOf = (service: ServiceView): string =>
  service.status === 'running'
    ? 'running'
    : !service.enabled
      ? 'paused'
      : service.lastRun?.status === 'failed'
        ? 'failed'
        : 'healthy';

/**
 * A stateful stand-in for the Services API (and for the one data route the screen reads a running job with). The list,
 * the timeline, the notices, the runs, the checks (a few real rules), the preview, save with the revision check, restore
 * default, pause / resume and Run now work over one in-memory state. A started run ends by itself the second time the
 * list is read (or with `advance()`). `calls` records what was asked.
 */
export function createServicesFake(options: ServicesFakeOptions = {}) {
  const initial = [...(options.services ?? sampleServices())];
  let services: ServiceView[] = [...initial];
  const defaults = new Map(initial.map((s) => [s.id, s]));
  const runs = new Map<string, ServiceRun[]>(
    initial.map((s) => [s.id, [...(options.runs?.[s.id] ?? (s.lastRun ? [s.lastRun] : []))]]),
  );
  let nextRun = 50;
  let readsWhileRunning = 0;
  const failures: ServicesFailures = { list: false, timeline: false, runs: false, preview: false };
  const calls = {
    list: 0,
    timeline: 0,
    notices: 0,
    saves: [] as { id: string; body: ServiceScheduleInput }[],
    previews: [] as { id: string; body: ServiceScheduleInput }[],
    resets: [] as string[],
    pauses: [] as string[],
    resumes: [] as string[],
    runs: [] as string[],
    runPages: [] as { id: string; page: number; pageSize: number }[],
  };
  const serverError = () => problemResponse({ title: 'The services are not available.', code: 'services.down' }, 500);
  const names = () => new Map(services.map((s) => [s.id, s.name]));
  const find = (id: string) => services.find((s) => s.id === id);
  const replace = (service: ServiceView) => {
    services = services.map((s) => (s.id === service.id ? service : s));
    return service;
  };
  const notFound = () => problemResponse({ title: 'The service does not exist.', code: 'services.not_found' }, 404);
  const readOnly = () =>
    problemResponse(
      { title: 'This entry is set elsewhere and cannot be changed here.', code: 'services.read_only' },
      409,
    );

  /** The findings against a draft of one service: a few of the real rules, plus the options' own. */
  function check(input: ServiceScheduleInput, service: ServiceView): ServiceIssue[] {
    const issue = (field: string, message: string, severity = 'error', code = 'services.invalid') =>
      buildServiceIssue({ serviceId: service.id, field, message, severity, code });
    const { schedule } = input;
    const found: ServiceIssue[] = [];
    if (!service.allowedScheduleTypes.includes(schedule.type)) {
      found.push(issue('schedule.type', 'This service cannot use that schedule type.'));
    }
    if (schedule.type === 'after') {
      const predecessor = find(schedule.after ?? '');
      if (!predecessor || predecessor.id === service.id || predecessor.schedule.after === service.id) {
        found.push(issue('schedule.after', 'The run-after chain would be circular.', 'error', 'services.circular'));
      }
    }
    if (
      (schedule.type === 'window' || schedule.type === 'interval') &&
      minutesOf(schedule.from ?? '00:00') >= minutesOf(schedule.to ?? '00:00')
    ) {
      found.push(issue('schedule.from', 'The window must start before it ends.'));
    }
    if (
      schedule.type === 'interval' &&
      (schedule.everyMinutes ?? 0) > minutesOf(schedule.to ?? '00:00') - minutesOf(schedule.from ?? '00:00')
    ) {
      found.push(issue('schedule.everyMinutes', 'The interval is longer than its window.'));
    }
    if (
      ['daily', 'window', 'interval'].includes(schedule.type) &&
      !schedule.tradingDaysOnly &&
      (schedule.days ?? []).length === 0
    ) {
      found.push(issue('schedule.days', 'Choose at least one day.'));
    }
    for (const parameter of service.parameters) {
      const value = (input.parameters as Record<string, unknown> | null | undefined)?.[parameter.name];
      if (typeof value === 'number' && (value < parameter.min || value > parameter.max)) {
        found.push(
          issue(
            `parameters.${parameter.name}`,
            `${parameter.label} must be between ${String(parameter.min)} and ${String(parameter.max)}.`,
          ),
        );
      }
    }
    if (
      service.id === serviceIds.eod &&
      schedule.type === 'daily' &&
      minutesOf(schedule.time ?? '23:59') < 15 * 60 + 35
    ) {
      found.push(
        issue(
          'schedule.time',
          'The end-of-day fetch before 15:35 may miss the last bars of the session.',
          'warning',
          'services.eod_too_early',
        ),
      );
    }
    return [...found, ...(options.extraIssues?.(input, service) ?? [])];
  }

  const timeline = (): ServicesTimeline => {
    const entries = services
      .filter((s) => s.enabled && !s.readOnly)
      .flatMap((s): TimelineEntry[] => {
        const spec = s.schedule;
        const base = {
          serviceId: s.id,
          name: s.name,
          marks: [],
          band: null,
          everyMinutes: null,
          followsServiceId: null,
        };
        if (spec.type === 'daily' || spec.type === 'weekly') {
          return [
            {
              ...base,
              kind: 'mark',
              marks: [{ time: spec.time ?? '00:00', at: `2026-10-13T${spec.time ?? '00:00'}:00+05:30` }],
            },
          ];
        }
        if (spec.type === 'after') {
          const lead = find(spec.after ?? '');
          const time = lead?.schedule.time;
          return time
            ? [
                {
                  ...base,
                  kind: 'mark',
                  followsServiceId: lead.id,
                  marks: [{ time, at: `2026-10-13T${time}:00+05:30` }],
                },
              ]
            : [];
        }
        if (spec.type === 'continuous') return [{ ...base, kind: 'band', band: { from: '00:00', to: '24:00' } }];
        return [
          {
            ...base,
            kind: 'band',
            band: { from: spec.from ?? '09:15', to: spec.to ?? '15:30' },
            everyMinutes: spec.everyMinutes ?? null,
          },
        ];
      });
    return { ...buildTimeline({ entries }), ...options.timeline };
  };

  const notices = (): ServiceNotice[] =>
    options.notices
      ? [...options.notices]
      : services.flatMap((s) =>
          s.execution.notifyOnFailure && s.status !== 'running' && s.lastRun?.status === 'failed'
            ? [
                {
                  id: `service:failed:${s.lastRun.id}`,
                  kind: 'service_failed',
                  severity: 'error',
                  text: `${s.name} failed: ${s.lastRun.message ?? 'no reason was recorded'}`,
                  serviceId: s.id,
                  at: s.lastRun.finishedAt,
                },
              ]
            : [],
        );

  /** Ends a run in progress the way `runOutcome` says. */
  function finishRun(serviceId: string) {
    const service = find(serviceId);
    const run = service?.lastRun;
    if (!service || run?.status !== 'running') return;
    const outcome = options.runOutcome ?? 'succeeded';
    const finished: ServiceRun = {
      ...run,
      status: outcome,
      finishedAt: '2026-10-13T10:05:00+05:30',
      message:
        outcome === 'failed'
          ? 'Dhan refused the access token.'
          : outcome === 'skipped'
            ? 'The Data account is not connected.'
            : (options.runMessage ?? null),
    };
    runs.set(serviceId, [finished, ...(runs.get(serviceId) ?? []).filter((r) => r.id !== run.id)]);
    replace({ ...service, lastRun: finished, status: statusOf({ ...service, status: 'idle', lastRun: finished }) });
  }

  const advance = () => {
    for (const service of services) finishRun(service.id);
  };

  const applySave = (service: ServiceView, input: ServiceScheduleInput, issues: ServiceIssue[]): ServiceView => {
    const values = (input.parameters ?? {}) as Record<string, unknown>;
    const draft: ServiceView = {
      ...service,
      enabled: input.enabled,
      schedule: input.schedule,
      scheduleWords: wordsOf(input.schedule, names()),
      execution: {
        retries: input.retries,
        timeoutMinutes: input.timeoutMinutes,
        catchUp: input.catchUp,
        notifyOnFailure: input.notifyOnFailure,
      },
      parameters: service.parameters.map((p) => ({ ...p, value: values[p.name] ?? p.value })),
      issues,
      isDefault: false,
      revision: service.revision + 1,
    };
    return replace({
      ...draft,
      status: statusOf(draft),
      nextRun: draft.enabled ? (nextStarts(draft.schedule)[0] ?? null) : null,
    });
  };

  const handlers: RequestHandler[] = [
    http.get('/api/services', () => {
      calls.list += 1;
      if (failures.list) return serverError();
      if (services.some((s) => s.status === 'running')) {
        readsWhileRunning += 1;
        if ((options.autoAdvance ?? true) && readsWhileRunning >= 2) {
          readsWhileRunning = 0;
          advance();
        }
      }
      return HttpResponse.json({ items: services });
    }),
    http.get('/api/services/timeline', () => {
      calls.timeline += 1;
      return failures.timeline ? serverError() : HttpResponse.json(timeline());
    }),
    http.get('/api/services/notices', () => {
      calls.notices += 1;
      return HttpResponse.json({ items: notices() });
    }),
    http.get('/api/services/:id/runs', ({ params, request }) => {
      const url = new URL(request.url);
      const page = Number(url.searchParams.get('page') ?? 1);
      const pageSize = Number(url.searchParams.get('pageSize') ?? 25);
      const id = String(params.id);
      calls.runPages.push({ id, page, pageSize });
      if (failures.runs) return serverError();
      if (!find(id)) return notFound();
      const all = runs.get(id) ?? [];
      return HttpResponse.json({
        items: all.slice((page - 1) * pageSize, page * pageSize),
        page,
        pageSize,
        total: all.length,
      });
    }),
    http.post('/api/services/:id/schedule/preview', async ({ params, request }) => {
      const body = (await request.json()) as ServiceScheduleInput;
      const id = String(params.id);
      calls.previews.push({ id, body });
      const service = find(id);
      if (failures.preview) return serverError();
      if (!service) return notFound();
      if (service.readOnly) return readOnly();
      const issues = check(body, service);
      const hasError = issues.some((i) => i.severity === 'error');
      return HttpResponse.json({
        issues,
        nextRuns: hasError ? [] : nextStarts(body.schedule),
        scheduleWords: wordsOf(body.schedule, names()),
      });
    }),
    http.put('/api/services/:id/schedule', async ({ params, request }) => {
      const body = (await request.json()) as ServiceScheduleInput;
      const id = String(params.id);
      calls.saves.push({ id, body });
      const service = find(id);
      if (!service) return notFound();
      if (service.readOnly) return readOnly();
      if (body.revision !== service.revision) {
        return problemResponse(
          { title: 'The service was changed elsewhere.', code: 'services.revision_changed', status: 409 },
          409,
        );
      }
      const issues = check(body, service);
      const errors = issues.filter((i) => i.severity === 'error');
      if (errors.length > 0) {
        return problemResponse(
          {
            title: 'The schedule is not valid.',
            code: 'services.invalid',
            status: 400,
            errors: Object.fromEntries(errors.map((e) => [e.field, [e.message]])),
            issues: errors,
          },
          400,
        );
      }
      return HttpResponse.json(applySave(service, body, issues));
    }),
    http.post('/api/services/:id/schedule/reset', ({ params }) => {
      const id = String(params.id);
      calls.resets.push(id);
      const service = find(id);
      const original = defaults.get(id);
      if (!service || !original) return notFound();
      if (service.readOnly) return readOnly();
      return HttpResponse.json(
        replace({
          ...original,
          lastRun: service.lastRun,
          status: service.status,
          isDefault: true,
          revision: service.revision + 1,
        }),
      );
    }),
    http.post('/api/services/:id/pause', ({ params }) => {
      const id = String(params.id);
      calls.pauses.push(id);
      const service = find(id);
      if (!service) return notFound();
      if (service.readOnly) return readOnly();
      const paused = { ...service, enabled: false, nextRun: null, revision: service.revision + 1 };
      return HttpResponse.json(replace({ ...paused, status: statusOf(paused) }));
    }),
    http.post('/api/services/:id/resume', ({ params }) => {
      const id = String(params.id);
      calls.resumes.push(id);
      const service = find(id);
      if (!service) return notFound();
      if (service.readOnly) return readOnly();
      const resumed = {
        ...service,
        enabled: true,
        nextRun: nextStarts(service.schedule)[0] ?? null,
        revision: service.revision + 1,
      };
      return HttpResponse.json(replace({ ...resumed, status: statusOf(resumed) }));
    }),
    http.post('/api/services/:id/run', ({ params }) => {
      const id = String(params.id);
      calls.runs.push(id);
      const service = find(id);
      if (!service) return notFound();
      if (service.readOnly) return readOnly();
      if (service.status === 'running') {
        return problemResponse(
          { title: `${service.name} is already running.`, code: 'services.already_running', status: 409 },
          409,
        );
      }
      nextRun += 1;
      const run: ServiceRun = {
        id: serviceRunId(nextRun),
        serviceId: id,
        trigger: 'manual',
        attempt: 1,
        startedAt: at,
        finishedAt: null,
        status: 'running',
        message: null,
        dataJobId: id === serviceIds.eod ? dataJobId(30) : null,
      };
      runs.set(id, [run, ...(runs.get(id) ?? [])]);
      replace({ ...service, status: 'running', lastRun: run });
      readsWhileRunning = 0;
      return HttpResponse.json(run, { status: 202 });
    }),
    // The data job the end-of-day run started: running (Dhan step) until the run ends.
    http.get('/api/data/jobs/:id', ({ params }) => {
      const id = String(params.id);
      const live = services.some((s) => s.lastRun?.dataJobId === id && s.status === 'running');
      return HttpResponse.json(
        live
          ? buildRunningJob({
              id,
              trigger: 'daily',
              steps: ['dhan', 'aggregate'],
              currentPhase: 'dhan',
              dhan: buildDhanPhase({ status: 'running', finishedAt: null, done: 120, total: 500 }),
            })
          : buildDataJob({ id }),
      );
    }),
  ];

  return {
    handlers,
    calls,
    failures,
    /** Ends the runs in progress (as `runOutcome` says). */
    advance,
    /** The services as the API would list them now. */
    services: () => services,
    /** Someone else saved the service: it has other times and a newer revision. */
    changeElsewhere: (id: string, patch: Partial<ScheduleSpec> = { time: '15:55' }) => {
      const service = find(id);
      if (service) {
        replace({
          ...service,
          schedule: { ...service.schedule, ...patch },
          scheduleWords: wordsOf({ ...service.schedule, ...patch }, names()),
          revision: service.revision + 1,
          isDefault: false,
        });
      }
    },
    /** Starts a run as the scheduler would (no click): the service shows as running on the next read. */
    startScheduledRun: (id: string) => {
      const service = find(id);
      if (!service) return;
      nextRun += 1;
      const run: ServiceRun = {
        id: serviceRunId(nextRun),
        serviceId: id,
        trigger: 'scheduled',
        attempt: 1,
        startedAt: at,
        finishedAt: null,
        status: 'running',
        message: null,
        dataJobId: id === serviceIds.eod ? dataJobId(30) : null,
      };
      runs.set(id, [run, ...(runs.get(id) ?? [])]);
      replace({ ...service, status: 'running', lastRun: run });
    },
  };
}

export type ServicesFake = ReturnType<typeof createServicesFake>;
