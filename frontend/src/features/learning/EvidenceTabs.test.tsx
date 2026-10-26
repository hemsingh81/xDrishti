import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { lossTags, rule, smart } from '@/test/evidenceFixtures';
import { mountLearning } from '@/test/learningFixtures';
import { problemResponse, server } from '@/test/server';
import type { Schemas } from '@/shared/api';
import { renderLearning } from './testing';

type Windows = Schemas['EntryWindowReportView'];
type Meta = Schemas['MetaPolicyReportView'];

const none = (code: string) => () => problemResponse({ title: 'None stored.', code }, 404);
const noStudies = () => {
  server.use(
    http.get('/api/learning/entry-windows', none('learning.entry_windows_not_found')),
    http.get('/api/learning/meta-policy', none('learning.meta_policy_not_found')),
  );
};

const totals = {
  candidates: 100,
  kept: 80,
  skipped: 20,
  meanKept: 0.15,
  meanSkipped: -0.3,
  baselinePerCandidate: 0.1,
  policyPerCandidate: 0.12,
  lift: 0.02,
  randomLift: 0.01,
};
const windowsStudy = (over: Partial<Windows> = {}): Windows => ({
  schemaVersion: 1,
  id: 4,
  outcome: 'Proposed',
  knobs: {
    bucketMinutes: 30,
    minWindowTrades: 30,
    minLift: 0.03,
    priorStrength: 20,
    minFoldTrades: 5,
    minEvaluableFolds: 2,
    holdOutMonths: 6,
  },
  rows: 120,
  from: '2025-01-01',
  to: '2026-06-30',
  usedFolds: 3,
  outsideSession: 0,
  cells: [
    {
      cellKey: 'ORB|intraday|X',
      status: 'Proposed',
      cellTrades: 120,
      cellWinRate: 0.5,
      windowFrom: '09:15',
      windowTo: '10:45',
      windowTrades: 40,
      keptShare: 0.33,
      shrunkRate: 0.58,
      lift: 0.08,
      meanRInside: 0.2,
      meanROutside: null,
      removesMajority: false,
      foldsEvaluable: 3,
      foldsHeld: 3,
      foldsRequired: 2,
      pooledLift: 0.06,
      reasons: ['held in 3 of 3 folds <b>x</b>'],
    },
  ],
  limits: ['Optimistic where chosen on all scores.'],
  basis: 'gross',
  sources: [],
  text: 't limit: l',
  builtBy: 'cli:xd',
  createdAt: '2026-10-26T09:00:00Z',
  ...over,
});
const metaStudy = (over: Partial<Meta> = {}): Meta => ({
  schemaVersion: 1,
  id: 2,
  outcome: 'NoPolicy',
  reasons: ['There is no champion model.'],
  knobs: {
    split: 'None',
    minKeptShare: 0.5,
    minLiftR: 0.02,
    minSkillR: 0.1,
    significanceZ: 1.645,
    trials: 1,
    holdOutMonths: 6,
  },
  rows: 0,
  from: null,
  to: null,
  usedFolds: 0,
  folds: [],
  pooled: totals,
  recent: totals,
  policy: [],
  limits: ['Advisory: nothing uses a policy until P7.'],
  basis: 'gross',
  sources: [],
  text: 't limit: l',
  builtBy: 'cli:xd',
  createdAt: '2026-10-26T09:00:00Z',
  ...over,
});

describe('Smart rules tab', () => {
  beforeEach(noStudies);

  it('with no plan stored prints five empty rules, the reason and no digit', async () => {
    mountLearning();
    server.use(http.get('/api/learning/smart-rules', () => HttpResponse.json(smart())));
    renderLearning('?tab=smart-rules');

    expect(await screen.findByText('Market gate')).toBeInTheDocument();
    expect(screen.getAllByText(/No plan stored yet \(the nightly service is paused/)).toHaveLength(5);
    await waitFor(() => {
      expect(screen.getAllByText('Not evaluable')).toHaveLength(5);
    });
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

  it('with no study stored the entry-window and meta-policy rules name the command and carry no digit', async () => {
    mountLearning();
    server.use(http.get('/api/learning/smart-rules', () => HttpResponse.json(smart())));
    renderLearning('?tab=smart-rules');

    const windows = await screen.findByRole('region', { name: 'Entry windows' });
    expect(await within(windows).findByText(/None stored yet/)).toBeInTheDocument();
    expect(windows).toHaveTextContent('learn windows');
    expect(screen.getByRole('region', { name: 'Meta-labelling' })).toHaveTextContent('learn policy');
  });

  it('shows a stored window study with the window as text, the folds, the reasons as text and a non-value as its word', async () => {
    mountLearning();
    server.use(
      http.get('/api/learning/smart-rules', () => HttpResponse.json(smart())),
      http.get('/api/learning/entry-windows', () => HttpResponse.json(windowsStudy())),
    );
    renderLearning('?tab=smart-rules');

    const windows = await screen.findByRole('region', { name: 'Entry windows' });
    expect(await within(windows).findByText(/window 09:15 to 10:45/)).toBeInTheDocument();
    expect(windows).toHaveTextContent('Held in 3 of 3 evaluable folds');
    expect(windows).toHaveTextContent('Stored study #4 by cli:xd');
    expect(within(windows).getByText(/<b>x<\/b>/)).toBeInTheDocument();
    expect(windows).toHaveTextContent('Optimistic where chosen');
  });

  it('labels a window study that proposed nothing by its status', async () => {
    mountLearning();
    server.use(
      http.get('/api/learning/smart-rules', () => HttpResponse.json(smart())),
      http.get('/api/learning/entry-windows', () =>
        HttpResponse.json(
          windowsStudy({
            outcome: 'NoWindowProposed',
            cells: [{ ...windowsStudy().cells[0]!, status: 'NotEnoughEvidence', windowFrom: null, windowTo: null }],
          }),
        ),
      ),
    );
    renderLearning('?tab=smart-rules');

    const windows = await screen.findByRole('region', { name: 'Entry windows' });
    expect(await within(windows).findByText(/Not enough evidence, no window/)).toBeInTheDocument();
  });

  it('shows a stored NoPolicy result as a result, with no digit drawn for the folds that do not exist', async () => {
    mountLearning();
    server.use(
      http.get('/api/learning/smart-rules', () => HttpResponse.json(smart())),
      http.get('/api/learning/meta-policy', () => HttpResponse.json(metaStudy())),
    );
    renderLearning('?tab=smart-rules');

    const meta = await screen.findByRole('region', { name: 'Meta-labelling' });
    expect(await within(meta).findByText('No policy')).toBeInTheDocument();
    expect(meta).toHaveTextContent('There is no champion model.');
    expect(meta).not.toHaveTextContent('Out of sample over');
  });

  it('shows a stored policy evaluation with kept and skipped R', async () => {
    mountLearning();
    server.use(
      http.get('/api/learning/smart-rules', () => HttpResponse.json(smart())),
      http.get('/api/learning/meta-policy', () =>
        HttpResponse.json(
          metaStudy({
            outcome: 'DoesNotHelp',
            reasons: ['Taking everything is as good.'],
            usedFolds: 4,
            folds: [
              {
                index: 1,
                testStart: '2026-01-01',
                testEnd: '2026-03-31',
                evaluable: true,
                reason: null,
                thresholds: [],
                totals,
              },
            ],
            policy: [{ group: '*', threshold: 0.55, rows: 300, keptShare: 0.8 }],
          }),
        ),
      ),
    );
    renderLearning('?tab=smart-rules');

    const meta = await screen.findByRole('region', { name: 'Meta-labelling' });
    expect(await within(meta).findByText('Does not help')).toBeInTheDocument();
    expect(meta).toHaveTextContent('Out of sample over 4 usable folds');
    expect(meta).toHaveTextContent('take at 0.55 or more');
  });

  it('shows the error of a study read that fails other than with 404', async () => {
    mountLearning();
    server.use(
      http.get('/api/learning/smart-rules', () => HttpResponse.json(smart())),
      http.get('/api/learning/entry-windows', () =>
        problemResponse({ title: 'Study cannot be read.', code: 'x.y' }, 500),
      ),
    );
    renderLearning('?tab=smart-rules');

    expect(await screen.findByText(/Study cannot be read/)).toBeInTheDocument();
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
