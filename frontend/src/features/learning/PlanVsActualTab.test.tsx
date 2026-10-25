import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { buildGradedPlanVsActual, buildPlanVsActual, metric, mountLearning } from '@/test/learningFixtures';
import { problemResponse, server } from '@/test/server';
import { renderLearning } from './testing';

describe('Plan vs actual', () => {
  it('with plans but no fills prints the counts, draws no bar and says why the chain is empty', async () => {
    mountLearning({ planVsActual: buildPlanVsActual() });
    renderLearning('?tab=plan');

    expect(
      (await screen.findAllByText(/No ticket has been graded on fills yet; fills arrive with paper and live trading/))
        .length,
    ).toBeGreaterThan(0);
    const chain = screen.getByRole('list', { name: 'From plan to actual' });
    expect(within(chain).getAllByText('No records yet')).toHaveLength(5);
    expect(chain.querySelectorAll('[aria-hidden]:not(svg)')).toHaveLength(0);
    expect(screen.getByText('Approved ticket lines')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Plan versus actual evidence' })).toHaveTextContent(
      'realised fills, not simulated',
    );
    expect(screen.getByText(/Exit slippage: not decomposed/)).toBeInTheDocument();
  });

  it('prints the hand-worked chain and the per-strategy rows', async () => {
    mountLearning({ planVsActual: buildGradedPlanVsActual() });
    renderLearning('?tab=plan');

    const chain = await screen.findByRole('list', { name: 'From plan to actual' });
    expect(chain).toHaveTextContent('+0.75 R');
    expect(chain).toHaveTextContent('-0.0875');
    expect(chain).toHaveTextContent('+0.57 R');
    const table = screen.getByRole('table', { name: 'Plan versus actual by strategy' });
    expect(within(table).getByText('ORB')).toBeInTheDocument();
    expect(table).toHaveTextContent('+0.34 R');
  });

  it('below the minimum every step is insufficient and no digit is drawn for it', async () => {
    const view = buildGradedPlanVsActual();
    mountLearning({
      planVsActual: {
        ...view,
        chain: view.chain.map((s) => ({
          ...s,
          metric: { state: 'insufficient', number: null, reason: 'n 4 of at least 15' },
        })),
      },
    });
    renderLearning('?tab=plan');

    const chain = await screen.findByRole('list', { name: 'From plan to actual' });
    expect(within(chain).getAllByText('Insufficient data')).toHaveLength(5);
    expect(chain).toHaveTextContent('n 4 of at least 15');
    expect(chain).not.toHaveTextContent('0.75');
  });

  it('counts automatic approvals apart from the owner and shows the missed triggers', async () => {
    mountLearning({ planVsActual: buildPlanVsActual({ missed: { count: 2, totalPlanR: metric(1.5) } }) });
    renderLearning('?tab=plan');

    expect(await screen.findByText(/Missed triggers/)).toHaveTextContent('2 ticket(s), total plan R +1.50 R');
    expect(screen.getByText('Approved automatically')).toBeInTheDocument();
    expect(screen.getByText('Approved by the owner')).toBeInTheDocument();
  });

  it('shows the server message for a refused range and renders server text as text', async () => {
    server.use(
      http.get('/api/reports/plan-vs-actual', () =>
        problemResponse({ title: 'From is after to.', code: 'reports.invalid' }, 422),
      ),
    );
    renderLearning('?tab=plan&from=2026-03-02&to=2026-03-01');

    expect(await screen.findByText(/From is after to/)).toBeInTheDocument();
  });

  it('does not interpret markup in server text', async () => {
    mountLearning({
      planVsActual: buildPlanVsActual({
        notDecomposed: [{ step: 'exit_slippage', reason: '<img src=x alt="injected"> reason' }],
      }),
    });
    renderLearning('?tab=plan');

    expect(await screen.findByText(/<img src=x alt="injected"> reason/)).toBeInTheDocument();
    expect(screen.queryByAltText('injected')).not.toBeInTheDocument();
  });

  it('shows the Execution gap tile as not evaluable on the overview when nothing is graded', async () => {
    mountLearning();
    renderLearning();

    const label = await screen.findByText('Execution gap (actual minus plan, R)', { selector: 'p' });
    const card = label.closest('.MuiCard-root');
    expect(card).not.toBeNull();
    expect(within(card as HTMLElement).getByText('Not evaluable')).toBeInTheDocument();
  });
});
