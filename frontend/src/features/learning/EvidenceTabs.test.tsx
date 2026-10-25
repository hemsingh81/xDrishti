import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mountLearning } from '@/test/learningFixtures';
import { problemResponse, server } from '@/test/server';
import type { Schemas } from '@/shared/api';
import { renderLearning } from './testing';

type SmartRules = Schemas['SmartRulesView'];
type LossTags = Schemas['LossTagsView'];

const outcome = { state: 'not_evaluable', value: null, reason: 'needs real taken trades', command: 'P10/P11' };
const rule = (id: string, name: string, over: Partial<Schemas['SmartRuleView']> = {}): Schemas['SmartRuleView'] => ({
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
const smart = (over: Partial<SmartRules> = {}): SmartRules => ({
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
const lossTags = (over: Partial<LossTags> = {}): LossTags => ({
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

describe('Smart rules tab', () => {
  it('with no plan stored prints five empty rules, the reason and no digit', async () => {
    mountLearning();
    server.use(http.get('/api/learning/smart-rules', () => HttpResponse.json(smart())));
    renderLearning('?tab=smart-rules');

    expect(await screen.findByText('Market gate')).toBeInTheDocument();
    expect(screen.getAllByText(/No plan stored yet \(the nightly service is paused/)).toHaveLength(5);
    expect(screen.getAllByText('Not evaluable')).toHaveLength(5);
    expect(screen.getAllByText('P10/P11').length).toBeGreaterThan(0);
    const region = screen.getByRole('region', { name: 'Market gate' });
    expect(region.textContent.replace('P10/P11', '')).not.toMatch(/\d/);
  });

  it('prints counts where recorded, a named non-value where not, and the flagged plan', async () => {
    mountLearning();
    server.use(
      http.get('/api/learning/smart-rules', () =>
        HttpResponse.json(
          smart({
            state: 'observed',
            plansRead: 3,
            from: '2026-03-02',
            to: '2026-03-04',
            rules: [
              rule('gate', 'Market gate', {
                state: 'observed',
                unavailableOn: 2,
                moved: 1,
                notRecorded: null,
                reasons: [{ code: 'gate_no_trade', count: 1 }],
              }),
              rule('meta', 'Meta-labelling', {
                state: 'not_recorded',
                notRecorded: 'the plan rows carry no code for this rule',
              }),
            ],
            flaggedPlans: [{ planId: 9, forDate: '2026-03-04', code: 'rule_removed_half' }],
          }),
        ),
      ),
    );
    renderLearning('?tab=smart-rules');

    const gate = await screen.findByRole('region', { name: 'Market gate' });
    expect(within(gate).getByText('gate_no_trade')).toBeInTheDocument();
    expect(gate).toHaveTextContent('Ran on');
    expect(gate).toHaveTextContent('a plan row does not prove that a rule ran');
    const meta = screen.getByRole('region', { name: 'Meta-labelling' });
    expect(meta).toHaveTextContent('the plan rows carry no code for this rule');
    expect(screen.getByText(/Flagged for review: a rule removed half or more/)).toBeInTheDocument();
  });

  it('shows the error', async () => {
    mountLearning();
    server.use(
      http.get('/api/learning/smart-rules', () =>
        problemResponse({ title: 'Plans cannot be read.', code: 'x.y' }, 500),
      ),
    );
    renderLearning('?tab=smart-rules');

    expect(await screen.findByText(/Plans cannot be read/)).toBeInTheDocument();
  });
});

describe('Loss tags tab', () => {
  it('prints counts, R lost, not-evaluable growing tags, worst losses as text and the not-built list', async () => {
    mountLearning();
    server.use(http.get('/api/learning/loss-tags', () => HttpResponse.json(lossTags())));
    renderLearning('?tab=loss-tags');

    const table = await screen.findByRole('table', { name: 'ORB|15m|BUY|intraday|FIXED tags' });
    expect(table).toHaveTextContent('gap through stop');
    expect(table).toHaveTextContent('-1.80 R');
    expect(screen.getByText(/needs 60 losses \(two windows of 30\)/)).toBeInTheDocument();
    expect(screen.getByText(/<b>gap<\/b> 0.8 R/)).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Tags not built' })).toHaveTextContent('against_regime');
    expect(screen.getByRole('region', { name: 'Loss tags evidence' })).toBeInTheDocument();
  });

  it('with no usable run says to choose runs and draws no cell', async () => {
    mountLearning();
    server.use(
      http.get('/api/learning/loss-tags', () =>
        HttpResponse.json(
          lossTags({
            state: 'empty',
            tradesRead: 0,
            losses: 0,
            cells: [],
            worst: [],
            evidence: {
              basis: 'unknown',
              n: 0,
              runs: [],
              defaultRunSet: true,
              skipped: ['abc: style or exit is not recorded'],
              unavailableInputs: [],
            },
          }),
        ),
      ),
    );
    renderLearning('?tab=loss-tags');

    expect(await screen.findByText(/Choose runs: add run=KEY:STYLE:EXIT/)).toBeInTheDocument();
    expect(screen.getByText(/style or exit is not recorded/)).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('shows a refused run as an error', async () => {
    mountLearning();
    server.use(
      http.get('/api/learning/loss-tags', () =>
        problemResponse({ title: 'No run abc is stored.', code: 'learning.run_not_found' }, 404),
      ),
    );
    renderLearning('?tab=loss-tags&run=abc:swing:X');

    expect(await screen.findByText(/No run abc is stored/)).toBeInTheDocument();
  });
});
