import { ApiError } from '@/shared/api';
import {
  buildFailedRun,
  buildMasterService,
  buildScheduleSpec,
  buildServiceIssue,
  buildServiceView,
  buildTimeline,
  buildTimelineEntry,
  serviceIds,
} from '@/test/servicesFixtures';
import { actionErrorText, issuesOf, problemText, saveErrorText } from './errors';
import { errorsByField, fieldName, listedIssues, mergeIssues, namedIssues, shownFields } from './issues';
import { executionFacts } from './labels';
import { nextRunText } from './nextRun';
import { earliestNextRun, outcomeOf } from './outcome';
import { formErrors, formOfService, inputOfForm } from './scheduleForm';
import { layoutTimeline, minutesOf, percentOf, timelineSummary } from './timeline';

const names = new Map([
  [serviceIds.eod, 'End-of-day data run'],
  [serviceIds.master, 'Instrument master refresh'],
]);
const apiError = (status: number, code: string, extra: object = {}) =>
  new ApiError(status, { title: `title of ${code}`, code, ...extra });

describe('timeline layout', () => {
  it('turns times into minutes and percents of the day', () => {
    expect(minutesOf('15:50')).toBe(950);
    expect(minutesOf('24:00')).toBe(1440);
    expect(percentOf('12:00')).toBe(50);
  });

  it('puts labels that would overlap in different rows, and writes late ones to the left of their dot', () => {
    const layout = layoutTimeline(
      buildTimeline({
        entries: [
          buildTimelineEntry({ marks: [{ time: '15:50', at: 'x' }] }),
          buildTimelineEntry({
            serviceId: 'b',
            name: 'Instrument master refresh',
            marks: [{ time: '15:55', at: 'x' }],
          }),
          buildTimelineEntry({ serviceId: 'c', name: 'Backup', marks: [{ time: '03:00', at: 'x' }] }),
          buildTimelineEntry({ serviceId: 'd', name: 'Nightly', marks: [{ time: '20:00', at: 'x' }] }),
        ],
      }),
    );

    const byName = Object.fromEntries(layout.marks.map((m) => [m.serviceId, m]));
    expect(byName.c?.lane).toBe(0);
    expect(byName.c?.flip).toBe(false);
    expect(byName.eod?.lane).not.toBe(byName.b?.lane);
    expect(byName.d?.flip).toBe(true);
    expect(byName.eod?.flip).toBe(false);
    expect(layout.markLanes).toBeGreaterThanOrEqual(2);
  });

  it('describes a start that follows another service by the service it follows', () => {
    const layout = layoutTimeline(
      buildTimeline({
        entries: [
          buildTimelineEntry(),
          buildTimelineEntry({
            serviceId: 'b',
            name: 'Quality',
            followsServiceId: serviceIds.eod,
            marks: [{ time: '15:50', at: 'x' }],
          }),
        ],
      }),
    );

    expect(layout.marks.find((m) => m.serviceId === 'b')).toMatchObject({
      after: true,
      label: '↳ 15:50 Quality',
      description: 'Quality starts after End-of-day data run finishes, about 15:50',
    });
  });

  it('gives each band its own row and says dashed for an interval', () => {
    const layout = layoutTimeline(
      buildTimeline({
        entries: [
          buildTimelineEntry({
            serviceId: 'a',
            name: 'A',
            kind: 'band',
            marks: [],
            band: { from: '09:00', to: '10:00' },
          }),
          buildTimelineEntry({
            serviceId: 'b',
            name: 'B',
            kind: 'band',
            marks: [],
            band: { from: '09:15', to: '15:30' },
            everyMinutes: 1,
          }),
        ],
      }),
    );

    expect(layout.bands.map((b) => [b.lane, b.dashed, b.description])).toEqual([
      [0, false, 'A is up from 09:00 to 10:00'],
      [1, true, 'B runs every 1 minutes from 09:15 to 15:30'],
    ]);
  });

  it('summarises the day, with and without a session', () => {
    expect(timelineSummary(buildTimeline())).toBe('market 09:15–15:30 · review cutoff 16:30');
    expect(timelineSummary(buildTimeline({ marketOpen: null, marketClose: null }))).toBe(
      'no market session · review cutoff 16:30',
    );
  });
});

describe('the schedule form', () => {
  const eod = buildServiceView();

  it('starts from the stored schedule, execution settings and parameters', () => {
    const form = formOfService(eod);

    expect(form).toMatchObject({
      enabled: true,
      type: 'daily',
      time: '15:50',
      tradingDaysOnly: true,
      retries: '2',
      timeoutMinutes: '60',
      catchUp: true,
      notifyOnFailure: true,
      parameters: { lookbackDays: '5', fetchGaps: true },
    });
    expect(form.days).toEqual(['mon', 'tue', 'wed', 'thu', 'fri']);
  });

  it('falls back to sensible values for fields a schedule does not carry', () => {
    const form = formOfService(
      buildMasterService({
        schedule: { type: 'continuous', tradingDaysOnly: false },
        allowedScheduleTypes: ['continuous'],
      }),
    );

    expect(form).toMatchObject({
      type: 'continuous',
      time: '16:00',
      from: '09:15',
      to: '15:30',
      everyMinutes: '5',
      after: '',
    });
  });

  it('sends only the fields of the type, with trading days as Monday to Friday', () => {
    const base = formOfService(buildMasterService());
    const input = (patch: object) => inputOfForm({ ...base, ...patch }, buildMasterService()).schedule;

    expect(input({ type: 'daily', time: '10:00', tradingDaysOnly: true })).toEqual({
      type: 'daily',
      time: '10:00',
      days: ['mon', 'tue', 'wed', 'thu', 'fri'],
      tradingDaysOnly: true,
    });
    expect(input({ type: 'weekly', weekday: 'sat', time: '10:00' })).toEqual({
      type: 'weekly',
      time: '10:00',
      weekday: 'sat',
      tradingDaysOnly: false,
    });
    expect(input({ type: 'after', after: 'eod' })).toEqual({ type: 'after', after: 'eod', tradingDaysOnly: false });
    expect(input({ type: 'interval', from: '09:15', to: '15:30', everyMinutes: '5', days: ['mon'] })).toEqual({
      type: 'interval',
      from: '09:15',
      to: '15:30',
      everyMinutes: 5,
      days: ['mon'],
      tradingDaysOnly: false,
    });
    expect(input({ type: 'continuous' })).toEqual({ type: 'continuous', tradingDaysOnly: false });
  });

  it('sends numbers as numbers, the revision it was read at, and the parameters by name', () => {
    const body = inputOfForm(
      { ...formOfService(eod), retries: '4', parameters: { lookbackDays: '9', fetchGaps: false } },
      eod,
    );

    expect(body).toMatchObject({
      retries: 4,
      timeoutMinutes: 60,
      parameters: { lookbackDays: 9, fetchGaps: false },
      revision: 3,
    });
  });

  it('finds what is missing before anything is sent', () => {
    const base = formOfService(eod);
    const errors = (patch: object) => formErrors({ ...base, ...patch }, eod);

    expect(errors({})).toEqual({});
    expect(errors({ time: '' })).toEqual({ 'schedule.time': 'Enter a time.' });
    expect(errors({ retries: '-1' }).retries).toBe('Between 0 and 5.');
    expect(errors({ timeoutMinutes: '0' }).timeoutMinutes).toBe('Between 1 and 240 minutes.');
    expect(errors({ tradingDaysOnly: false, days: [] })['schedule.days']).toBe('Choose at least one day.');
    expect(errors({ type: 'after', after: '' })['schedule.after']).toBe('Choose the service to run after.');
    expect(errors({ type: 'interval', everyMinutes: '0' })['schedule.everyMinutes']).toBe('Between 1 and 120 minutes.');
    expect(errors({ type: 'window', from: '', to: '25:00' })).toMatchObject({
      'schedule.from': 'Enter a start time.',
      'schedule.to': 'Enter an end time.',
    });
    expect(errors({ parameters: { lookbackDays: '31', fetchGaps: true } })['parameters.lookbackDays']).toBe(
      'Between 1 and 30.',
    );
    expect(errors({ parameters: { lookbackDays: '', fetchGaps: true } })['parameters.lookbackDays']).toBe(
      'Enter a number.',
    );
    expect(errors({ parameters: { lookbackDays: '1.5', fetchGaps: true } })['parameters.lookbackDays']).toBe(
      'Enter a whole number.',
    );
  });
});

describe('issues', () => {
  const warning = buildServiceIssue();
  const error = buildServiceIssue({ field: 'retries', severity: 'error', message: 'Too many.' });

  it('names fields in the words of the dialog, parameters by their label', () => {
    expect(fieldName('schedule.time')).toBe('Time');
    expect(fieldName('retries')).toBe('Retries');
    expect(fieldName('parameters.lookbackDays', buildServiceView().parameters)).toBe('Gap look-back');
    expect(fieldName('something.else')).toBe('something.else');
  });

  it('lists every issue once, errors first, with the service’s name', () => {
    const list = namedIssues([
      buildServiceView({ issues: [warning, error] }),
      buildMasterService({ issues: [warning] }),
    ]);

    expect(list.map((n) => [n.issue.severity, n.serviceName])).toEqual([
      ['error', 'End-of-day data run'],
      ['warning', 'End-of-day data run'],
    ]);
  });

  it('writes an error on the field it is about while that field is shown, and lists it otherwise', () => {
    const shown = shownFields('daily', []);
    const timeError = buildServiceIssue({ severity: 'error', message: 'Bad time.' });
    const afterError = buildServiceIssue({ field: 'schedule.after', severity: 'error', message: 'Circle.' });
    const other = buildServiceIssue({ serviceId: serviceIds.master, field: 'schedule.time', severity: 'error' });

    expect(errorsByField([timeError, afterError, other, warning], serviceIds.eod, shown)).toEqual({
      'schedule.time': 'Bad time.',
    });
    expect(listedIssues([timeError, afterError, other, warning], serviceIds.eod, shown)).toEqual([
      afterError,
      other,
      warning,
    ]);
  });

  it('joins several errors of a field and shows the fields of the type', () => {
    expect(
      errorsByField(
        [error, buildServiceIssue({ field: 'retries', severity: 'error', message: 'Really.' })],
        serviceIds.eod,
        shownFields('daily', []),
      ),
    ).toEqual({ retries: 'Too many. Really.' });
    expect([...shownFields('interval', [])].sort()).toEqual(
      [
        'retries',
        'schedule.days',
        'schedule.everyMinutes',
        'schedule.from',
        'schedule.to',
        'schedule.type',
        'timeoutMinutes',
      ].sort(),
    );
  });

  it('merges lists without repeating a finding', () => {
    expect(mergeIssues([warning, error], [warning])).toEqual([warning, error]);
  });
});

describe('words', () => {
  it('says retries, timeout and catch-up as chips', () => {
    expect(executionFacts({ retries: 2, timeoutMinutes: 60, catchUp: true, notifyOnFailure: true })).toEqual([
      '2 retries',
      '60 min timeout',
      'Catches up',
    ]);
    expect(executionFacts({ retries: 1, timeoutMinutes: 5, catchUp: false, notifyOnFailure: false })).toEqual([
      '1 retry',
      '5 min timeout',
    ]);
  });

  it('says the next run, or why there is none', () => {
    expect(nextRunText(buildServiceView(), names)).toBe('Tue 13 Oct 15:50');
    expect(nextRunText(buildServiceView({ enabled: false, nextRun: null }), names)).toBeNull();
    expect(
      nextRunText(
        buildServiceView({
          nextRun: null,
          schedule: { type: 'after', after: serviceIds.master, tradingDaysOnly: false },
        }),
        names,
      ),
    ).toBe('After Instrument master refresh');
    expect(
      nextRunText(buildServiceView({ nextRun: null, schedule: { type: 'continuous', tradingDaysOnly: false } }), names),
    ).toBe('Always on');
    expect(
      nextRunText(buildServiceView({ nextRun: null, schedule: buildScheduleSpec({ type: 'window' }) }), names),
    ).toBeNull();
  });

  it('says how a run ended, for the toast', () => {
    expect(outcomeOf(buildServiceView())).toEqual({ text: 'End-of-day data run finished.', tone: 'success' });
    expect(outcomeOf(buildServiceView({ lastRun: buildFailedRun() }))).toEqual({
      text: 'End-of-day data run failed: Dhan refused the access token.',
      tone: 'error',
    });
    expect(outcomeOf(buildServiceView({ lastRun: buildFailedRun({ status: 'skipped', message: null }) }))).toEqual({
      text: 'End-of-day data run was skipped.',
      tone: 'info',
    });
  });

  it('finds the earliest next run of the services that are on', () => {
    expect(
      earliestNextRun([
        buildServiceView({ nextRun: '2026-10-13T15:50:00+05:30' }),
        buildMasterService({ nextRun: '2026-10-13T10:00:00+05:30' }),
        buildServiceView({ id: 'paused', enabled: false, nextRun: '2026-10-13T01:00:00+05:30' }),
      ]),
    ).toBe('2026-10-13T10:00:00+05:30');
    expect(earliestNextRun([buildServiceView({ nextRun: null })])).toBeNull();
  });
});

describe('errors', () => {
  it('reads the issues of a refused save, naming the edited service when the API leaves it out', () => {
    const error = apiError(400, 'services.invalid', {
      issues: [
        { field: 'retries', severity: 'error', code: 'a', message: 'Too many.' },
        { serviceId: serviceIds.master, field: 'schedule.after', severity: 'error', code: 'b', message: 'Circle.' },
      ],
    });

    expect(issuesOf(error, serviceIds.eod).map((i) => [i.serviceId, i.field])).toEqual([
      [serviceIds.eod, 'retries'],
      [serviceIds.master, 'schedule.after'],
    ]);
    expect(issuesOf(apiError(500, 'x'), serviceIds.eod)).toEqual([]);
    expect(issuesOf(new Error('x'), serviceIds.eod)).toEqual([]);
  });

  it('says a failed save in words, or nothing when the dialog says it on the fields', () => {
    expect(saveErrorText(apiError(400, 'services.invalid'))).toBeNull();
    expect(saveErrorText(apiError(409, 'services.revision_changed'))).toBeNull();
    expect(saveErrorText(apiError(409, 'services.read_only'))).toBe(
      'This entry is set elsewhere and cannot be changed here.',
    );
    expect(saveErrorText(apiError(404, 'services.not_found'))).toBe('This service no longer exists.');
    expect(saveErrorText(apiError(500, 'x'))).toBe('title of x');
  });

  it('says a failed action in words', () => {
    expect(actionErrorText(apiError(409, 'services.already_running'), 'Backup', 'x')).toBe(
      'Backup is already running. This page shows its progress.',
    );
    expect(actionErrorText(apiError(404, 'services.not_found'), 'Backup', 'x')).toBe('Backup no longer exists.');
    expect(actionErrorText(apiError(409, 'services.read_only'), 'Backup', 'x')).toBe(
      'Backup is set elsewhere and cannot be changed here.',
    );
    expect(actionErrorText(new Error('boom'), 'Backup', 'x')).toBe(
      'Could not reach the server. Check your connection and try again.',
    );
    expect(problemText(new ApiError(500), 'fallback')).toBe('fallback');
  });
});
