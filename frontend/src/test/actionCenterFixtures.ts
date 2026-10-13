import { http, HttpResponse } from 'msw';
import type {
  ActionCounts,
  ActionItem,
  ActionList,
  ActionNumbers,
  ActionThresholds,
  ActionThresholdsView,
  PortfolioNotice,
  PriceAlert,
  PriceAlertPage,
} from '@/shared/api';
import { holdingIds, ids } from './fixtures';

// Made-up ids of the Action center and the price alerts.
const alertId = (n: number) => `00000000-0000-4000-8000-0000000012${String(n).padStart(2, '0')}`;
export const alertIds = { first: alertId(1), second: alertId(2), third: alertId(3), fourth: alertId(4) } as const;

/** The master-list ids of the sample instruments (`sampleInstruments`: RELIANCE is the first, TCS the second). */
export const instrumentIds = {
  reliance: '00000000-0000-4000-8000-000000000201',
  tcs: '00000000-0000-4000-8000-000000000202',
  hdfcBank: '00000000-0000-4000-8000-000000000203',
  /** Not in the sample master list (INFY): items name it, the pickers never find it. */
  infy: '00000000-0000-4000-8000-000000000299',
} as const;

export const buildActionNumbers = (overrides: Partial<ActionNumbers> = {}): ActionNumbers => ({ ...overrides });

/** A high "below cost" item for TCS: down 23.4 % on ₹40,000 invested. Override what the test is about. */
export function buildActionItem(overrides: Partial<ActionItem> = {}): ActionItem {
  return {
    id: `below_cost:${holdingIds.tcs}`,
    rule: 'below_cost',
    severity: 'high',
    accountId: ids.main,
    accountName: 'Test Dhan Main',
    holdingId: holdingIds.tcs,
    instrumentId: instrumentIds.tcs,
    symbol: 'TCS',
    title: 'Down 23.4 % from cost',
    detail: 'TCS is worth ₹30,640 against ₹40,000 invested, a loss of ₹9,360.',
    numbers: buildActionNumbers({ pnlPct: -23.4, pnl: -9_360, invested: 40_000, value: 30_640, thresholdPct: 20 }),
    suggestions: ['open', 'set_alert', 'add_to_basket', 'snooze', 'done'],
    state: 'open',
    snoozedUntil: null,
    ...overrides,
  };
}

const reliance = {
  holdingId: holdingIds.reliance,
  instrumentId: instrumentIds.reliance,
  symbol: 'RELIANCE',
};

/** Seven open items, one for each rule, in the API's order: severity, then rule priority. */
export const sampleActionItems = (): ActionItem[] => [
  buildActionItem(),
  buildActionItem({
    id: `lagging_nifty:${holdingIds.infy}`,
    rule: 'lagging_nifty',
    severity: 'medium',
    holdingId: holdingIds.infy,
    instrumentId: instrumentIds.infy,
    symbol: 'INFY',
    title: 'Behind Nifty 50 by 12.7 points',
    detail: 'INFY returned 4.1 % a year against 16.8 % for the same money in Nifty 50.',
    numbers: buildActionNumbers({ heldDays: 412, xirrPct: 4.1, benchmarkXirrPct: 16.8, gapPp: -12.7, thresholdPp: 10 }),
  }),
  buildActionItem({
    id: `overweight:${holdingIds.reliance}`,
    rule: 'overweight',
    severity: 'medium',
    ...reliance,
    title: 'Takes up 42.1 % of the account',
    detail: 'RELIANCE is 42.1 % of Test Dhan Main, above the 22.5 % limit for 4 positions.',
    numbers: buildActionNumbers({ weightPct: 42.1, thresholdPct: 22.5, positions: 4, value: 116_100 }),
  }),
  buildActionItem({
    id: `sector_concentration:${ids.main}:3f2a9c1d4e5b`,
    rule: 'sector_concentration',
    severity: 'medium',
    holdingId: null,
    instrumentId: null,
    symbol: null,
    title: 'Energy is 41.2 % of the account',
    detail: 'Energy holds 41.2 % of Test Dhan Main, above the 35 % limit.',
    numbers: buildActionNumbers({
      sector: 'Energy',
      sectorSharePct: 41.2,
      thresholdPct: 35,
      sectorValue: 116_100,
      positions: 1,
      largestSymbol: 'RELIANCE',
      largestHoldingId: holdingIds.reliance,
      largestValue: 116_100,
    }),
    suggestions: ['open', 'snooze', 'done'],
  }),
  buildActionItem({
    id: `large_gain:${holdingIds.infy}`,
    rule: 'large_gain',
    severity: 'info',
    holdingId: holdingIds.infy,
    instrumentId: instrumentIds.infy,
    symbol: 'INFY',
    title: 'Up 31.2 % on cost',
    detail: 'INFY is worth ₹52,480 against ₹40,000 invested.',
    numbers: buildActionNumbers({ pnlPct: 31.2, pnl: 12_480, invested: 40_000, value: 52_480, thresholdPct: 25 }),
  }),
  buildActionItem({
    id: `long_term_soon:${holdingIds.reliance}`,
    rule: 'long_term_soon',
    severity: 'info',
    ...reliance,
    title: 'One lot turns 12 months old soon',
    detail: 'A lot of RELIANCE in profit turns 12 months old in 18 days: check the tax impact before selling early.',
    numbers: buildActionNumbers({
      pnlPct: 16.1,
      lots: [{ acquiredOn: '2025-10-31', qty: 10, longTermOn: '2026-10-31', daysLeft: 18, gainPct: 16.1 }],
    }),
    suggestions: ['open', 'set_alert', 'snooze', 'done'],
  }),
  buildActionItem({
    id: `not_in_basket:${holdingIds.tcs}`,
    rule: 'not_in_basket',
    severity: 'info',
    title: 'In no basket',
    detail: 'TCS is in none of your baskets.',
    numbers: buildActionNumbers({ invested: 40_000, value: 30_640 }),
    suggestions: ['open', 'add_to_basket', 'snooze', 'done'],
  }),
];

const rules = [
  'below_cost',
  'large_gain',
  'overweight',
  'sector_concentration',
  'long_term_soon',
  'lagging_nifty',
  'not_in_basket',
] as const;

export function buildActionCounts(overrides: Partial<ActionCounts> = {}): ActionCounts {
  return {
    open: 0,
    snoozed: 0,
    done: 0,
    byRule: Object.fromEntries(rules.map((rule) => [rule, 0])),
    bySeverity: { high: 0, medium: 0, info: 0 },
    unavailable: { lagging: 0 },
    noPrice: 0,
    ...overrides,
  };
}

/** The counts of a set of items (as the API counts them: states, and rules and severities of the open ones). */
export function countsOf(all: readonly ActionItem[], extra: Partial<ActionCounts> = {}): ActionCounts {
  const open = all.filter((item) => item.state === 'open');
  const counts = buildActionCounts({
    open: open.length,
    snoozed: all.filter((item) => item.state === 'snoozed').length,
    done: all.filter((item) => item.state === 'done').length,
    ...extra,
  });
  for (const item of open) {
    counts.byRule[item.rule] = (counts.byRule[item.rule] ?? 0) + 1;
    counts.bySeverity[item.severity] = (counts.bySeverity[item.severity] ?? 0) + 1;
  }
  return counts;
}

/** The answer for the items shown, with the counts of the whole set. */
export const buildActionList = (items: readonly ActionItem[], extra: Partial<ActionCounts> = {}): ActionList => ({
  items: [...items],
  counts: countsOf(items, extra),
});

export const defaultThresholds: ActionThresholds = {
  belowCostPct: 10,
  belowCostHighPct: 20,
  gainPct: 25,
  overweightFloorPct: 15,
  overweightMultiple: 1.5,
  minPositions: 3,
  concentrationPct: 35,
  lagPp: 10,
  lagDays: 180,
  longTermWindowDays: 30,
};

export const buildThresholdsView = (overrides: Partial<ActionThresholdsView> = {}): ActionThresholdsView => ({
  values: defaultThresholds,
  defaults: defaultThresholds,
  isDefault: true,
  updatedBy: null,
  updatedAt: null,
  ...overrides,
});

/** A customised view: gain flagged at 40 %, written by the owner on 12 Oct. */
export const buildCustomisedThresholds = (): ActionThresholdsView =>
  buildThresholdsView({
    values: { ...defaultThresholds, gainPct: 40 },
    isDefault: false,
    updatedBy: 'example@invalid.test',
    updatedAt: '2026-10-12T09:30:00Z',
  });

/** An active alert on RELIANCE: at or below ₹2,500. */
export const buildPriceAlert = (overrides: Partial<PriceAlert> = {}): PriceAlert => ({
  id: alertIds.first,
  instrumentId: instrumentIds.reliance,
  symbol: 'NSE:RELIANCE',
  name: 'Reliance Industries',
  direction: 'at_or_below',
  level: 2_500,
  note: null,
  status: 'active',
  createdAt: '2026-10-10T05:00:00Z',
  triggeredAt: null,
  triggerPrice: null,
  ...overrides,
});

export const buildTriggeredAlert = (overrides: Partial<PriceAlert> = {}): PriceAlert =>
  buildPriceAlert({
    id: alertIds.second,
    symbol: 'NSE:TCS',
    name: 'Tata Consultancy Services',
    instrumentId: instrumentIds.tcs,
    direction: 'at_or_above',
    level: 3_900,
    note: 'Book half',
    status: 'triggered',
    triggeredAt: '2026-10-12T09:40:00Z',
    triggerPrice: 3_912.5,
    ...overrides,
  });

export function buildAlertPage(items: readonly PriceAlert[], overrides: Partial<PriceAlertPage> = {}): PriceAlertPage {
  return {
    items: [...items],
    page: 1,
    pageSize: 50,
    total: items.length,
    activeCount: items.filter((alert) => alert.status === 'active').length,
    activeLimit: 200,
    ...overrides,
  };
}

export const buildPortfolioNotice = (overrides: Partial<PortfolioNotice> = {}): PortfolioNotice => ({
  id: 'portfolio:actions:2026-10-13',
  kind: 'actions_need_decision',
  severity: 'info',
  text: '5 holdings need a decision',
  at: '2026-10-13T04:30:00Z',
  alertId: null,
  instrumentId: null,
  ...overrides,
});

export const buildAlertNotice = (overrides: Partial<PortfolioNotice> = {}): PortfolioNotice =>
  buildPortfolioNotice({
    id: `portfolio:alert:${alertIds.second}`,
    kind: 'price_alert_triggered',
    severity: 'warn',
    text: 'TCS reached ₹3,912.50, at or above your ₹3,900 alert',
    at: '2026-10-12T09:40:00Z',
    alertId: alertIds.second,
    instrumentId: instrumentIds.tcs,
    ...overrides,
  });

/** Default API mocks: nothing needs a decision, no alerts, the default thresholds, no notices. The Action center fake replaces them. */
export const actionCenterHandlers = [
  http.get('/api/portfolio/actions', () => HttpResponse.json(buildActionList([]))),
  http.get('/api/portfolio/alerts', () => HttpResponse.json(buildAlertPage([]))),
  http.get('/api/portfolio/action-thresholds', () => HttpResponse.json(buildThresholdsView())),
  http.get('/api/portfolio/notices', () => HttpResponse.json({ items: [] })),
];
