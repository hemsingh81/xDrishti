import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import {
  buildDecayReport,
  buildKill,
  buildProposal,
  buildProposals,
  buildSeries,
  emptySeries,
  mountConfidence,
} from '@/test/confidenceFixtures';
import { buildCell, mountLearning } from '@/test/learningFixtures';
import { server } from '@/test/server';
import { renderLearning } from './testing';

afterEach(() => {
  server.events.removeAllListeners();
});

const twoStrategies = () =>
  mountLearning({ cells: { all: [buildCell({ strategy: 'orb' }), buildCell({ strategy: 'vwap' })] } });

describe('Decay tab', () => {
  it('shows the windows against their level, twice in a row, CUSUM, quarters and the reference', async () => {
    twoStrategies();
    const seen = mountConfidence();
    renderLearning('?tab=decay');

    const windows = await screen.findByRole('table', { name: /Edge series.*completed windows/ });
    const rows = within(windows).getAllByRole('row');
    expect(rows).toHaveLength(3);
    expect(rows[2]).toHaveTextContent('Below the level');
    expect(rows[1]).toHaveTextContent('At or above the level');
    expect(rows[2]).toHaveTextContent('-0.14 R');
    expect(rows[2]).toHaveTextContent('-0.09');

    expect(screen.getAllByText(/Alert: the latest window is below its level/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/No \(1 completed window/)).toHaveLength(1);
    expect(screen.getByText(/CUSUM alarm on 2026-06-10/)).toBeInTheDocument();

    const quarters = screen.getByRole('table', { name: /Edge series.*by quarter/ });
    expect(within(quarters).getByText('2026-Q2')).toBeInTheDocument();
    expect(within(quarters).getByText('Weak (below the floor)')).toBeInTheDocument();
    expect(screen.getAllByText(/Share of quarters below the floor: 50.0%/).length).toBeGreaterThan(0);

    expect(screen.getByText('Reference trades (N_ref)')).toBeInTheDocument();
    expect(screen.getByText(/a placeholder until CF-01/)).toBeInTheDocument();
    // The first strategy of the stored cells is read.
    expect(seen.strategies).toEqual(['orb']);
  });

  it('names the four kill-rule states with their numbers, and held and not evaluated as not a pass', async () => {
    twoStrategies();
    mountConfidence();
    renderLearning('?tab=decay');

    const table = await screen.findByRole('table', { name: 'Kill rules and their numbers' });
    const rowOf = (rule: RegExp) => within(table).getByRole('row', { name: rule });
    expect(rowOf(/Cost stress/)).toHaveTextContent('Fires');
    expect(rowOf(/Cost stress/)).toHaveTextContent('-0.04');
    expect(rowOf(/Decay twice in a row/)).toHaveTextContent('Holds');
    expect(rowOf(/Drawdown/)).toHaveTextContent('Not evaluated (not a pass)');
    // No number is drawn for a figure that was not evaluated.
    expect(rowOf(/Drawdown/)).toHaveTextContent('Not available');
    expect(rowOf(/Failure boundary/)).toHaveTextContent('Held (not a pass)');
    expect(rowOf(/Failure boundary/)).toHaveTextContent('proposal only');
  });

  it('lists the open and decided proposals with who and when, and the automatic setting OFF', async () => {
    twoStrategies();
    mountConfidence({
      proposals: buildProposals({
        items: [
          buildProposal({ id: 8 }),
          buildProposal({
            id: 7,
            rule: 'decay_twice',
            state: 'applied',
            actor: 'auto',
            decidedAt: '2026-11-03T05:00:00Z',
            autoSettingId: 2,
          }),
        ],
      }),
    });
    renderLearning('?tab=decay');

    const table = await screen.findByRole('table', { name: 'Kill proposals, newest first' });
    const rows = within(table).getAllByRole('row');
    expect(rows).toHaveLength(3);
    expect(rows[1]).toHaveTextContent('Open (proposed)');
    expect(rows[1]).toHaveTextContent('Not decided');
    expect(rows[1]).toHaveTextContent('orb|15m|BUY|intraday|fixed_r|*');
    expect(rows[2]).toHaveTextContent('Applied');
    expect(rows[2]).toHaveTextContent('auto, 3 Nov 2026');
    expect(screen.getByText(/OFF \(manual approval\)/)).toBeInTheDocument();
    expect(screen.getByText(/Every act here is the owner's, explicit and confirmed/)).toBeInTheDocument();
  });

  it('says what the automatic setting means when ON', async () => {
    twoStrategies();
    mountConfidence({
      proposals: buildProposals({
        settings: { auto: true, settingId: 3, stressMultiplier: 2, drawdownFactor: 2, settingsHash: 'h' },
      }),
    });
    renderLearning('?tab=decay');

    expect(await screen.findByText(/records the decision as "auto", never as an owner approval/)).toBeInTheDocument();
  });

  it('says there is no proposal when none was written', async () => {
    twoStrategies();
    mountConfidence();
    renderLearning('?tab=decay');

    const table = await screen.findByRole('table', { name: 'Kill proposals, newest first' });
    expect(within(table).getByText(/No kill proposal has been written/)).toBeInTheDocument();
  });

  it('keeps small figures distinguishable beside a verdict (not rounded to the same 0.00)', async () => {
    twoStrategies();
    const base = buildSeries();
    mountConfidence({
      decay: buildDecayReport({
        edge: buildSeries({
          windows: [{ ...base.windows[1]!, meanR: -0.094, threshold: -0.091, below: true }],
        }),
        kill: [buildKill({ rule: 'cost_stress', state: 'fires', value: -0.004, limit: 0.0001, text: 'At 2x costs.' })],
      }),
      proposals: buildProposals({
        items: [buildProposal({ rule: 'cost_stress', value: -0.004, limit: 0.0001 })],
      }),
    });
    renderLearning('?tab=decay');

    const rule = await screen.findByRole('row', { name: /Cost stress/ });
    expect(rule).toHaveTextContent('-0.004');
    expect(rule).toHaveTextContent('0.0001');
    const proposals = await screen.findByRole('table', { name: 'Kill proposals, newest first' });
    const proposal = within(proposals).getAllByRole('row')[1];
    expect(proposal).toHaveTextContent('-0.004');
    expect(proposal).toHaveTextContent('0.0001');
    const windows = screen.getByRole('table', { name: /Edge series.*completed windows/ });
    const row = within(windows).getAllByRole('row')[1];
    expect(row).toHaveTextContent('-0.094 R');
    expect(row).toHaveTextContent('-0.091');
    // The design effect is a quantity, not a gain: no plus sign.
    expect(row).toHaveTextContent('1.60');
    expect(row).not.toHaveTextContent('+1.60');
  });

  it('shows an unjudged series with its reason and no windows, never a pass', async () => {
    twoStrategies();
    mountConfidence({
      decay: buildDecayReport({
        reference: {
          state: 'unavailable',
          reason: 'no backtest trades before the epoch',
          trades: null,
          meanR: null,
          sigma: null,
          basis: null,
        },
        edge: emptySeries(),
        kill: [
          buildKill({ rule: 'cost_stress', state: 'not_evaluated', value: null, limit: null, text: 'No series.' }),
        ],
      }),
    });
    renderLearning('?tab=decay');

    expect(await screen.findByText(/no backtest trades before the epoch/)).toBeInTheDocument();
    expect(screen.getByText(/never judged/)).toBeInTheDocument();
    const windows = screen.getByRole('table', { name: /Edge series.*completed windows/ });
    expect(within(windows).getByText(/No completed window yet/)).toBeInTheDocument();
    expect(screen.getAllByText('Not judged: no reference').length).toBeGreaterThan(0);
    expect(screen.queryByText(/No alert/)).not.toBeInTheDocument();
  });

  it('shows the monitoring state while no window is complete', async () => {
    twoStrategies();
    mountConfidence({
      decay: buildDecayReport({
        edge: buildSeries({
          state: 'monitoring',
          trades: 20,
          pendingTrades: 20,
          windows: [],
          quarters: [],
          cusum: null,
          weakQuarterShare: null,
          streak: 0,
        }),
      }),
    });
    renderLearning('?tab=decay');

    expect((await screen.findAllByText('Monitoring: no completed window yet')).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/20 trades are waiting for the first one/).length).toBeGreaterThan(0);
  });

  it('shows the refusal for a strategy without a series', async () => {
    twoStrategies();
    mountConfidence();
    renderLearning('?tab=decay&strategy=ghost');

    expect(await screen.findByRole('alert')).toHaveTextContent('The strategy has no series.');
    expect(screen.queryByRole('table', { name: /completed windows/ })).not.toBeInTheDocument();
  });

  it('reads the chosen strategy when the picker changes', async () => {
    twoStrategies();
    const seen = mountConfidence({ decay: buildDecayReport({ strategy: 'vwap' }) });
    renderLearning('?tab=decay');
    // The first strategy has no series in this fake: refused.
    expect(await screen.findByRole('alert')).toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText('Strategy'), 'vwap');
    expect(await screen.findByRole('table', { name: /Edge series.*completed windows/ })).toBeInTheDocument();
    expect(seen.strategies).toContain('vwap');
  });

  it('shows an error when the proposals fail, without hiding the report', async () => {
    twoStrategies();
    mountConfidence();
    server.use(
      http.get('/api/confidence/kill-proposals', () => HttpResponse.json({ title: 'Down.' }, { status: 500 })),
    );
    renderLearning('?tab=decay');

    expect(await screen.findByRole('table', { name: /Edge series.*completed windows/ })).toBeInTheDocument();
    expect((await screen.findAllByRole('alert')).length).toBeGreaterThan(0);
  });

  it('says there is no strategy yet when no cell is stored', async () => {
    mountLearning({ cells: { all: [] } });
    mountConfidence();
    renderLearning('?tab=decay');

    expect(await screen.findByText(/No strategy to pick yet/)).toBeInTheDocument();
  });

  it('sends only reads on load: every owner act waits for a confirmation', async () => {
    twoStrategies();
    const seen = mountConfidence({ proposals: buildProposals({ items: [buildProposal()] }) });
    renderLearning('?tab=decay');

    await screen.findByRole('table', { name: 'Kill proposals, newest first' });
    const panel = screen.getByRole('tabpanel');
    expect(within(panel).getByRole('button', { name: 'Apply proposal 7' })).toBeInTheDocument();
    expect(seen.methods.every((method) => method === 'GET')).toBe(true);
  });
});
