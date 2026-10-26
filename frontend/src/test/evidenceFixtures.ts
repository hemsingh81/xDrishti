import type { Schemas } from '@/shared/api';

// Builders of the Smart rules and Loss tags views (shared by the tab tests and the browser runner, P8-16).
type SmartRules = Schemas['SmartRulesView'];
type LossTags = Schemas['LossTagsView'];

const outcome = { state: 'not_evaluable', value: null, reason: 'needs real taken trades', command: 'P10/P11' };
export const rule = (
  id: string,
  name: string,
  over: Partial<Schemas['SmartRuleView']> = {},
): Schemas['SmartRuleView'] => ({
  id,
  name,
  state: 'empty',
  ranOn: null,
  unavailableOn: null,
  partialOn: null,
  moved: null,
  reasons: [],
  notRecorded: 'no plan stored yet',
  outcome,
  ...over,
});
const ids: [string, string][] = [
  ['gate', 'Market gate'],
  ['throttle', 'Equity throttle'],
  ['meta', 'Meta-labelling'],
  ['windows', 'Entry windows'],
  ['correlation', 'Correlation screen'],
];
export const smart = (over: Partial<SmartRules> = {}): SmartRules => ({
  state: 'empty',
  plansRead: 0,
  from: null,
  to: null,
  source: 'plan item codes',
  rules: ids.map(([id, name]) => rule(id, name)),
  flaggedPlans: [],
  limits: ['A plan row stores a code only when a rule was unavailable.'],
  ...over,
});

const tags = (kind: string, count: number, lostR: number) => ({
  kind,
  count,
  share: count / 4,
  lostR,
});
export const lossTags = (over: Partial<LossTags> = {}): LossTags => ({
  state: 'ok',
  tradesRead: 10,
  losses: 4,
  cells: [
    {
      cellKey: 'ORB|15m|BUY|intraday|FIXED',
      trades: 10,
      losses: 4,
      untagged: 1,
      tags: [tags('gap_through_stop', 1, -1.8), tags('quick_stop', 1, -1)],
      growingState: 'not_evaluable',
      growingReason: 'needs 60 losses (two windows of 30)',
      growing: [],
    },
  ],
  worst: [
    {
      key: 't1',
      cellKey: 'ORB|15m|BUY|intraday|FIXED',
      r: -1.8,
      tags: [{ kind: 'gap_through_stop', detail: '<b>gap</b> 0.8 R' }],
    },
  ],
  comparisons: 0,
  expectedChanceFlags: 0,
  notBuilt: ['against_regime: needs the regime of each trade (P6-02)'],
  evidence: { basis: 'gross', n: 10, runs: [], defaultRunSet: true, skipped: [], unavailableInputs: [] },
  limits: ['Tags overlap.'],
  ...over,
});
