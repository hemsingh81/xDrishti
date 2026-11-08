import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { server } from './server';

type Summary = Schemas['ShadowSummaryView'];
const value = (number: number) => ({ state: 'value', number, reason: null });

export const buildShadowSummary = (over: Partial<Summary> = {}): Summary => ({
  enabled: true,
  hasRows: true,
  note: null,
  statement: 'Modelled fills; no queue, no latency.',
  separateCount: 'This is a separate count; the P10 exit criterion is untouched.',
  counts: {
    behavioural: 12,
    edgeRaw: 40,
    edgeEffective: 22.5,
    candidates: 90,
    edgeCandidates: 60,
    pending: 5,
    notTriggered: 10,
    notGradable: 5,
  },
  edge: {
    rawN: 40,
    effectiveN: 22.5,
    rho: 0.3,
    rhoIsPlaceholder: true,
    rhoNote: 'rho is a placeholder until CF-01.',
    sessions: 8,
    targetEffectiveN: 60,
    etaSessions: 14,
    etaNote: 'About 14 more sessions.',
  },
  groups: [
    { group: 'all', n: 40, meanR: value(0.12), low90: value(-0.05), high90: value(0.3), hitRate: value(0.55) },
    {
      group: 'rejected',
      n: 0,
      meanR: { state: 'empty', number: null, reason: 'no graded trades' },
      low90: value(0),
      high90: value(0),
      hitRate: value(0),
    },
  ],
  costs: { basis: 'Gross R (before charges)', gross: true, sheetIds: [], confirmed: false },
  chain: { sessions: 9, gaps: 1, lastSession: '2026-11-06', lastDigest: 'abc123digest' },
  sequential: {
    available: true,
    note: null,
    edgeState: 'caution',
    behaviouralState: 'monitoring',
    line: 'Edge caution, behavioural monitoring.',
  },
  ...over,
});

export function mountShadow(view: Summary = buildShadowSummary()) {
  server.use(http.get('/api/paper/shadow/summary', () => HttpResponse.json(view)));
}
