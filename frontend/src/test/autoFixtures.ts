import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';

/** Builders for the Auto screen tests (AU-18). */
export const buildAuto = (over: Partial<Schemas['AutoView']> = {}): Schemas['AutoView'] => ({
  on: false,
  complete: false,
  note: null,
  builder: 'nightly plan builder',
  chain: [{ serviceId: 'sim', name: 'Simulation', enabled: false, resumedByAuto: false }],
  preconditions: [
    { id: 'charges', label: 'Charge sheet confirmed', status: 'pass', detail: 'Confirmed.' },
    { id: 'data', label: 'Data fresh', status: 'unavailable', detail: 'No reading.' },
  ],
  night: { state: 'published', forDate: '2026-11-10', failedGate: null, reason: null },
  changes: [],
  noOrderNotice: 'No order is placed.',
  ...over,
});

export const buildSuggestion = (over: Partial<Schemas['AutoSuggestionView']> = {}): Schemas['AutoSuggestionView'] => ({
  itemId: 1,
  rank: 1,
  symbol: 'INFY',
  side: 'BUY',
  strategyId: 'orb',
  strategyVersionHash: 'abc',
  levels: null,
  reasons: ['Opening range breakout held.'],
  evidence: { n: '120', nEff: '80', conservativeExpectancyR: '0.15', regime: 'all regimes', dataAsOf: '2026-11-09' },
  why: null,
  ...over,
});

export const buildAutoScope = (over: Partial<Schemas['AutoScopeView']> = {}): Schemas['AutoScopeView'] => ({
  isSet: false,
  version: null,
  basketIds: [],
  instrumentIds: [],
  hash: null,
  changedBy: null,
  changedAt: null,
  summary: 'eligible',
  ...over,
});

export const buildAutoSuggestions = (
  over: Partial<Schemas['AutoSuggestionsView']> = {},
): Schemas['AutoSuggestionsView'] => ({
  forDate: '2026-11-11',
  planId: 5,
  planState: 'published',
  basis: 'basis',
  notice: 'Suggestions only. No order is placed.',
  scopeHash: null,
  suggestions: [buildSuggestion()],
  notSuggested: [],
  reserve: [],
  rejected: [],
  ...over,
});

/** Defaults: Auto OFF, no scope, no plan. */
export const autoHandlers = [
  http.get('/api/auto', () => HttpResponse.json(buildAuto())),
  http.get('/api/auto/scope', () => HttpResponse.json(buildAutoScope())),
  http.get('/api/auto/suggestions', () =>
    HttpResponse.json(
      { title: 'No plan.', status: 404, code: 'auto.no_plan' },
      { status: 404, headers: { 'content-type': 'application/problem+json' } },
    ),
  ),
];
