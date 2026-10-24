import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';

/** Default handlers: the plan list only, empty. Everything else of a plan is mocked by the test that needs it. */
export const planHandlers = [http.get('/api/plans', () => HttpResponse.json([]))];

/** Builders for the plan screen tests (P7-17). The ids are made up. */
export const accountA = '00000000-0000-4000-8000-0000000000a1';
export const accountB = '00000000-0000-4000-8000-0000000000b2';
/** 09:10 IST on 26 Oct 2026. */
export const cutoffIso = '2026-10-26T03:40:00Z';
/** Forty minutes before the cutoff. */
export const beforeCutoff = '2026-10-26T03:00:00Z';

export const buildPlan = (over: Partial<Schemas['PlanView']> = {}): Schemas['PlanView'] => ({
  id: 7,
  planKey: 'plan-2026-10-26',
  forDate: '2026-10-26',
  reviewCutoff: cutoffIso,
  version: 1,
  state: 'published',
  nextStates: ['inreview'],
  basis:
    'Proposed, provisional: no order exists, nothing here is approved. market gate unavailable (vix not known): judge the market yourself. Open book unavailable: no primary tickets.',
  provisional: true,
  notice: 'Armed is not an order.',
  limits: 'Replayed history, modelled fills, no hold-out.',
  createdAt: '2026-10-25T16:00:00Z',
  createdBy: 'rule:nightly',
  primary: 1,
  reserve: 1,
  rejected: 1,
  items: [],
  history: [],
  ...over,
});

export const buildItem = (over: Partial<Schemas['PlanItemView']> = {}): Schemas['PlanItemView'] => ({
  id: 11,
  rank: 1,
  group: 'primary',
  state: 'proposed',
  nextStates: ['approved', 'rejected'],
  symbol: 'AAA',
  side: 'BUY',
  strategyId: 'ORB',
  strategyVersionHash: 'abc',
  trigger: 'Break of 100.0',
  stop: 'Stop 95.0',
  target: 'Target 110.0',
  validity: 'Today only',
  reasons: [],
  evidence: {
    cellKey: 'orb|15m|long',
    trades: 142,
    dataRange: 'about 2.1 years, replayed history, modelled fills, no hold-out',
    pWinBasis: 'lower bound',
    pWin: 0.54,
    conservativeExpectancyR: 0.21,
    unavailable: ['events'],
    advisoryNote: null,
  },
  allocations: [{ accountId: accountA, mode: 'paper', quantity: 10 }],
  ...over,
});

export const planItems = (): Schemas['PlanItemView'][] => [
  buildItem(),
  buildItem({ id: 12, rank: 2, group: 'reserve', symbol: 'BBB', reasons: ['below_top_n'] }),
  buildItem({
    id: 13,
    rank: 3,
    group: 'rejected',
    state: null,
    nextStates: [],
    symbol: 'CCC',
    reasons: ['liquidity_too_low'],
    allocations: [],
  }),
];

export const buildTicket = (over: Partial<Schemas['ReviewTicketView']> = {}): Schemas['ReviewTicketView'] => ({
  itemId: 11,
  rank: 1,
  group: 'primary',
  state: 'proposed',
  symbol: 'AAA',
  side: 'BUY',
  checklistRecorded: true,
  canApprove: false,
  needsConfirmation: ['data_quality'],
  approveAllEligible: false,
  modification: null,
  confirmedDespite: null,
  accounts: [
    {
      accountId: accountA,
      mode: 'paper',
      quantity: 10,
      effectiveQuantity: 10,
      canApprove: false,
      checks: [
        { code: 'liquidity', outcome: 'pass', standing: 'passed', detail: 'ok' },
        { code: 'data_quality', outcome: 'unavailable', standing: 'needsconfirmation', detail: 'not wired' },
      ],
    },
  ],
  ...over,
});

export const cleanTicket = (over: Partial<Schemas['ReviewTicketView']> = {}) =>
  buildTicket({
    canApprove: true,
    needsConfirmation: [],
    approveAllEligible: true,
    accounts: [
      {
        accountId: accountA,
        mode: 'paper',
        quantity: 10,
        effectiveQuantity: 10,
        canApprove: true,
        checks: [{ code: 'liquidity', outcome: 'pass', standing: 'passed', detail: 'ok' }],
      },
    ],
    ...over,
  });

export const buildReview = (over: Partial<Schemas['PlanReviewView']> = {}): Schemas['PlanReviewView'] => ({
  planId: 7,
  forDate: '2026-10-26',
  state: 'published',
  reviewCutoff: cutoffIso,
  open: true,
  notice: 'Approved for watching: no order is placed and none exists.',
  tickets: [buildTicket(), buildTicket({ itemId: 12, rank: 2, group: 'reserve', symbol: 'BBB' })],
  ...over,
});

export const buildOutcome = (
  over: Partial<Schemas['ReviewDecisionOutcome']> = {},
): Schemas['ReviewDecisionOutcome'] => ({
  itemId: 11,
  state: 'approved',
  notice: 'Approved for watching: no order is placed and none exists.',
  overriddenCodes: [],
  ignored: [],
  ...over,
});
