import { http, HttpResponse } from 'msw';
import type { components } from '@/shared/api/schema';

type Calendar = components['schemas']['ResultsCalendarView'];
type Meeting = components['schemas']['ResultsMeetingView'];
type EventFilter = components['schemas']['EventFilterView'];
type FilterRow = components['schemas']['EventFilterRowView'];
type Evaluation = components['schemas']['ExtensionEvaluationView'];

export const buildMeeting = (over: Partial<Meeting> = {}): Meeting => ({
  symbol: 'INFY',
  instrumentId: '00000000-0000-4000-8000-0000000000e1',
  periodEnd: '2026-09-30',
  meetingDate: '2026-11-04',
  eventSession: '2026-11-04',
  days: 2,
  intimatedAt: '2026-10-28T10:00:00+05:30',
  revision: 1,
  ...over,
});

export const buildCalendar = (over: Partial<Calendar> = {}): Calendar => ({
  from: '2026-11-02',
  to: '2027-01-01',
  asOf: '2026-11-02T09:00:00+05:30',
  meetings: [],
  truncated: false,
  instrumentsWithoutEntry: { count: 0, symbols: [] },
  ...over,
});

export const buildFilterRow = (over: Partial<FilterRow> = {}): FilterRow => ({
  id: 1,
  enabled: true,
  resultsWithinDays: 2,
  approval: 'manual',
  decidedBy: 'owner',
  actor: 'owner',
  reason: 'first switch',
  settingSnapshot: '{}',
  verdictRef: null,
  changedAt: '2026-11-01T10:00:00+05:30',
  ...over,
});

export const buildEventFilter = (over: Partial<EventFilter> = {}): EventFilter => ({
  enabled: false,
  resultsWithinDays: 2,
  approval: 'manual',
  decidedBy: null,
  autoVerdict: 'unavailable',
  history: [],
  notActingBecause: [{ code: 'switch_off', count: null }],
  instrumentsTracked: 30,
  instrumentsWithEntry: 3,
  ...over,
});

export const buildEvaluation = (over: Partial<Evaluation> = {}): Evaluation => ({
  id: 'a'.repeat(64),
  family: 'filter:results',
  protocol: 'F',
  extensionVersion: null,
  versionKey: 'ev-rule-1:N=2',
  baseVersion: 'tf-1',
  verdict: 'NOT_EVALUABLE',
  trials: 3,
  factor: 1.5,
  foldUnit: 'quarter',
  foldsTotal: 8,
  foldsEvaluable: 6,
  rows: 100,
  flips: null,
  missing: ['seasons 8 of 20'],
  limits: ['Seasons, not trades, are the independent units; the intervals are wide.'],
  estimates: {
    window_sessions: '2',
    trades: 100,
    skipped: 12,
    taken: 70,
    unavailable: 18,
    skipped_share: 0.12,
    taken_share: 0.7,
    unavailable_share: 0.18,
    removed_share: 0.3,
    share_lost_to_no_upcoming_entry: 0.08,
    unavailable_by_reason: {
      no_entry: { trades: 4, share: 0.04 },
      only_cancelled: { trades: 0, share: 0 },
      no_upcoming_entry: { trades: 8, share: 0.08 },
    },
    mean_gap_r: 'unavailable',
  },
  evidence: { protocol: 'F', ledger_trials: 3, factor: 1.5 },
  inputs: {},
  holdOutRead: false,
  ...over,
});

/** Defaults for screens that only read these (plan tickets, Today): no meetings, the filter off. Tests bring their own. */
export const eventsHandlers = [
  http.get('/api/calendar/results', () => HttpResponse.json(buildCalendar())),
  http.get('/api/selection/event-filter', () => HttpResponse.json(buildEventFilter())),
];
