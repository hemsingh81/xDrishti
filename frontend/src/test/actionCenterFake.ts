import { http, HttpResponse } from 'msw';
import type {
  ActionItem,
  ActionThresholds,
  ActionThresholdsInput,
  ActionThresholdsView,
  PortfolioNotice,
  PriceAlert,
  PriceAlertInput,
} from '@/shared/api';
import {
  buildAlertPage,
  buildPriceAlert,
  buildThresholdsView,
  countsOf,
  defaultThresholds,
  sampleActionItems,
} from './actionCenterFixtures';
import { sampleInstruments } from './fixtures';
import { holdStep, pendingResponse } from './gate';
import { problemResponse } from './server';

export interface ActionCenterFakeOptions {
  /** The items of the Action center, in order (default: seven open ones, one per rule). */
  items?: readonly ActionItem[];
  alerts?: readonly PriceAlert[];
  thresholds?: ActionThresholdsView;
  notices?: readonly PortfolioNotice[];
  /** The API's active-alert limit (default 200); a small one makes the limit easy to reach. */
  alertLimit?: number;
  /** Counts the API reports besides the items'. */
  unavailableLagging?: number;
  noPrice?: number;
  /** Last prices by instrument id, for the "already true" refusal of a new alert and for the last close of the bars. */
  lastPrices?: Readonly<Record<string, number>>;
}

/** What a decision or a write answers while a test holds or breaks it. */
export interface ActionCenterFailures {
  actions: boolean;
  alerts: boolean;
  thresholds: boolean;
  /** The next decision gets this status (409, 404, 500) instead of being taken. */
  decision: number | null;
  cancel: number | null;
  /** Decisions and cancels wait until `releasePending()` (503 then): the screen can be looked at meanwhile. */
  hold: boolean;
  /** Decisions answer normally but one at a time, each when the test calls `releaseStep()`. */
  stepwise: boolean;
}

const decisionDay = '2026-10-20';
const severityRank: Record<string, number> = { high: 0, medium: 1, info: 2 };
const rulePriority = [
  'below_cost',
  'lagging_nifty',
  'overweight',
  'sector_concentration',
  'large_gain',
  'long_term_soon',
  'not_in_basket',
];
const priorityOf = (rule: string) => rulePriority.indexOf(rule);

const ranges: Record<keyof ActionThresholds, [number, number]> = {
  belowCostPct: [1, 90],
  belowCostHighPct: [2, 95],
  gainPct: [1, 1000],
  overweightFloorPct: [1, 100],
  overweightMultiple: [1, 10],
  minPositions: [2, 50],
  concentrationPct: [5, 100],
  lagPp: [1, 100],
  lagDays: [30, 3650],
  longTermWindowDays: [1, 365],
};

const invalid = (errors: Record<string, string[]>) =>
  HttpResponse.json(
    { title: 'One or more values are not valid.', status: 400, code: 'portfolio.invalid', errors },
    { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
  );

/**
 * A stateful stand-in for the Action center, price alert, threshold and notice endpoints. Decisions and alert changes
 * work over one in-memory state, so a test can act and look at the next read. `calls` records what was asked and
 * `failures` makes a read or a write fail. Use `fake.handlers` with `server.use(...)`.
 */
export function createActionCenterFake(options: ActionCenterFakeOptions = {}) {
  let items = [...(options.items ?? sampleActionItems())];
  let alerts = [...(options.alerts ?? [])];
  let thresholds = options.thresholds ?? buildThresholdsView();
  let nextAlert = 100;
  const limit = options.alertLimit ?? 200;
  const failures: ActionCenterFailures = {
    actions: false,
    alerts: false,
    thresholds: false,
    decision: null,
    cancel: null,
    hold: false,
    stepwise: false,
  };
  const calls = {
    actionReads: [] as URLSearchParams[],
    decisions: [] as { id: string; decision: string }[],
    alertReads: [] as URLSearchParams[],
    created: [] as PriceAlertInput[],
    cancelled: [] as string[],
    thresholdSaves: [] as ActionThresholdsInput[],
    thresholdResets: 0,
    noticeReads: 0,
    closeReads: [] as string[],
  };
  const down = () => problemResponse({ title: 'The service is not available.', code: 'portfolio.down' }, 500);
  const activeAlerts = () => alerts.filter((alert) => alert.status === 'active');

  const decide = (decision: 'snooze' | 'done' | 'reopen') =>
    http.post(`/api/portfolio/actions/:id/${decision}`, async ({ params }) => {
      const id = decodeURIComponent(String(params.id));
      calls.decisions.push({ id, decision });
      if (failures.hold) return pendingResponse();
      if (failures.stepwise) await holdStep();
      if (failures.decision !== null) {
        const status = failures.decision;
        const code = status === 404 ? 'portfolio.not_found' : status === 409 ? 'portfolio.conflict' : 'portfolio.down';
        return problemResponse({ title: 'The decision was refused.', code }, status);
      }
      const item = items.find((candidate) => candidate.id === id);
      if (!item) {
        return decision === 'reopen'
          ? HttpResponse.json({ id, state: 'open', snoozedUntil: null, item: null })
          : problemResponse({ title: 'That item is not on the list now.', code: 'portfolio.not_found' }, 404);
      }
      const state = decision === 'snooze' ? 'snoozed' : decision === 'done' ? 'done' : 'open';
      const changed: ActionItem = { ...item, state, snoozedUntil: state === 'snoozed' ? decisionDay : null };
      items = items.map((candidate) => (candidate.id === id ? changed : candidate));
      return HttpResponse.json({ id, state, snoozedUntil: changed.snoozedUntil, item: changed });
    });

  const handlers = [
    http.get('/api/portfolio/actions', ({ request }) => {
      const params = new URL(request.url).searchParams;
      calls.actionReads.push(params);
      if (failures.actions) return down();
      const account = params.get('accountId');
      const scoped = items.filter((item) => !account || item.accountId === account);
      const status = params.get('status') ?? 'open';
      const shown = scoped
        .filter((item) => status === 'all' || item.state === status)
        .sort(
          (a, b) =>
            (severityRank[a.severity] ?? 3) - (severityRank[b.severity] ?? 3) ||
            priorityOf(a.rule) - priorityOf(b.rule),
        );
      return HttpResponse.json({
        items: shown,
        counts: countsOf(scoped, {
          unavailable: { lagging: options.unavailableLagging ?? 0 },
          noPrice: options.noPrice ?? 0,
        }),
      });
    }),
    decide('snooze'),
    decide('done'),
    decide('reopen'),
    http.get('/api/portfolio/action-thresholds', () => (failures.thresholds ? down() : HttpResponse.json(thresholds))),
    http.put('/api/portfolio/action-thresholds', async ({ request }) => {
      const body = (await request.json()) as ActionThresholdsInput;
      calls.thresholdSaves.push(body);
      const errors: Record<string, string[]> = {};
      for (const [key, [min, max]] of Object.entries(ranges)) {
        const value = body[key as keyof ActionThresholds];
        if (value === null || value < min || value > max)
          errors[key] = [`${key} must be between ${String(min)} and ${String(max)}.`];
      }
      if (Object.keys(errors).length > 0) return invalid(errors);
      const values = body as ActionThresholds;
      thresholds = buildThresholdsView({
        values,
        isDefault: JSON.stringify(values) === JSON.stringify(defaultThresholds),
        updatedBy: 'example@invalid.test',
        updatedAt: '2026-10-13T05:00:00Z',
      });
      return HttpResponse.json(thresholds);
    }),
    http.post('/api/portfolio/action-thresholds/reset', () => {
      calls.thresholdResets += 1;
      thresholds = buildThresholdsView();
      return HttpResponse.json(thresholds);
    }),
    http.get('/api/portfolio/alerts', ({ request }) => {
      const params = new URL(request.url).searchParams;
      calls.alertReads.push(params);
      if (failures.alerts) return down();
      const status = params.get('status') ?? 'active';
      const page = Number(params.get('page') ?? 1);
      const pageSize = Number(params.get('pageSize') ?? 50);
      const matches = alerts.filter((alert) => status === 'all' || alert.status === status);
      return HttpResponse.json(
        buildAlertPage(matches.slice((page - 1) * pageSize, page * pageSize), {
          page,
          pageSize,
          total: matches.length,
          activeCount: activeAlerts().length,
          activeLimit: limit,
        }),
      );
    }),
    http.post('/api/portfolio/alerts', async ({ request }) => {
      const body = (await request.json()) as PriceAlertInput;
      calls.created.push(body);
      const instrument = sampleInstruments.find((candidate) => candidate.id === body.instrumentId);
      if (!instrument) return invalid({ instrumentId: ['Choose a share or ETF from the master list.'] });
      if ((body.note ?? '').length > 200) return invalid({ note: ['The note can be at most 200 characters.'] });
      const last = options.lastPrices?.[instrument.id];
      if (last !== undefined && body.level !== null) {
        const holds = body.direction === 'at_or_below' ? last <= body.level : last >= body.level;
        if (holds) {
          return invalid({
            level: [
              `The last price is ₹${last.toFixed(2)}, which is already ${body.direction === 'at_or_below' ? 'at or below' : 'at or above'} this level.`,
            ],
          });
        }
      }
      if (activeAlerts().length >= limit) {
        return problemResponse({ title: 'Too many active alerts.', code: 'portfolio.alert_limit' }, 409);
      }
      nextAlert += 1;
      const created = buildPriceAlert({
        id: `00000000-0000-4000-8000-0000000012${String(nextAlert)}`,
        instrumentId: instrument.id,
        symbol: instrument.symbol,
        name: instrument.displayName,
        direction: body.direction ?? 'at_or_below',
        level: body.level ?? 0,
        note: body.note,
        createdAt: '2026-10-13T05:00:00Z',
      });
      alerts = [created, ...alerts];
      return HttpResponse.json(created);
    }),
    http.post('/api/portfolio/alerts/:id/cancel', async ({ params }) => {
      const id = String(params.id);
      calls.cancelled.push(id);
      if (failures.hold) return pendingResponse();
      if (failures.cancel !== null)
        return problemResponse({ title: 'Failed.', code: 'portfolio.down' }, failures.cancel);
      const alert = alerts.find((candidate) => candidate.id === id);
      if (!alert) return problemResponse({ title: 'No such alert.', code: 'portfolio.not_found' }, 404);
      const result = alert.status === 'active' ? { ...alert, status: 'cancelled' } : alert;
      alerts = alerts.map((candidate) => (candidate.id === id ? result : candidate));
      return HttpResponse.json(result);
    }),
    http.get('/api/portfolio/notices', () => {
      calls.noticeReads += 1;
      return HttpResponse.json({ items: options.notices ?? [] });
    }),
    http.get('/api/data/instruments/:id/bars', ({ params }) => {
      const id = String(params.id);
      calls.closeReads.push(id);
      const close = options.lastPrices?.[id];
      return HttpResponse.json({
        instrumentId: id,
        symbol: 'NSE:TEST',
        name: 'Test',
        timeframe: '1D',
        items:
          close === undefined
            ? []
            : [
                {
                  ts: '2026-10-12T00:00:00+05:30',
                  open: close,
                  high: close,
                  low: close,
                  close,
                  volume: 1,
                  bars: 1,
                  complete: true,
                  source: 'dhan',
                },
              ],
      });
    }),
  ];

  return {
    handlers,
    calls,
    failures,
    /** The items as the API holds them now (states changed by decisions). */
    items: () => items,
    alerts: () => alerts,
    thresholds: () => thresholds,
    /** Adds an alert without a request (an alert that triggered by itself, say). */
    addAlert: (alert: PriceAlert) => {
      alerts = [alert, ...alerts];
    },
  };
}

export type ActionCenterFake = ReturnType<typeof createActionCenterFake>;
