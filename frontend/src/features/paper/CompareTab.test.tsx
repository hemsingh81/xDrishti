import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildCompare, buildThinCompare, mountCompare } from '@/test/compareFixtures';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { PaperPage } from './components/PaperPage';

pinClock('2026-10-26T09:00:00+05:30');

const open = () => renderRoutes([{ path: '/paper', element: <PaperPage /> }], { initialPath: '/paper?tab=compare' });

describe('Paper backtest vs paper tab', () => {
  it('shows the figures, the band in words, the verdicts, the fan with its table and the basis', async () => {
    mountCompare();
    open();

    expect((await screen.findAllByText('Paper mean R')).length).toBeGreaterThan(0);
    expect(screen.getByText(/paper is inside the backtest band, rank 86 of 100/)).toBeInTheDocument();
    expect(screen.getByText(/within the backtest 90th percentile/)).toBeInTheDocument();
    expect(screen.getAllByText('MET').length).toBe(2);
    expect(screen.getByText(/point estimate; 90% interval includes 0/)).toBeInTheDocument();
    expect(screen.getAllByText(/no order was placed or exists/).length).toBeGreaterThan(0);
    expect(screen.getByText(/simulated on plan prices, all proposals: n 80/)).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /Cumulative R over 3 trades: paper \+1\.50R/ })).toBeInTheDocument();
    await userEvent.click(screen.getByText('Show the same numbers as a table'));
    const fan = await screen.findByRole('table', { name: 'Fan of cumulative R' });
    expect(within(fan).getByText('+3.50')).toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'Cells of the counted paper trades' })).toBeInTheDocument();
  });

  it('shows the early warning and says below the band in words', async () => {
    mountCompare(
      buildCompare({
        earlyWarning: 'paper is clearly worse than the backtest allows: stop and review before more trades',
        band: { ...buildCompare().band, position: 'below' },
        criteria: {
          expectancy: { verdict: 'not_met', label: 'paper mean -0.400R is below the backtest band' },
          drawdown: buildCompare().criteria.drawdown,
        },
      }),
    );
    open();

    expect(await screen.findByText(/paper is clearly worse than the backtest allows/)).toBeInTheDocument();
    expect(screen.getByText(/Simulated; not an instruction to trade/)).toBeInTheDocument();
    expect(screen.getByText(/paper is below the backtest band/)).toBeInTheDocument();
    expect(screen.getByText('NOT MET')).toBeInTheDocument();
  });

  it('shows words and reasons, never a digit, when the sample is too small', async () => {
    mountCompare(buildThinCompare());
    open();

    expect(await screen.findAllByText(/12 counted trades; at least 30 are needed/)).not.toHaveLength(0);
    expect(screen.getAllByText('NOT EVALUABLE').length).toBe(2);
    expect(screen.getByText(/No fan: the band is not a figure yet/)).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(screen.queryByText(/inside the backtest band/)).not.toBeInTheDocument();
  });

  it('labels the mean as not after-cost when the owner has not confirmed the sheet', async () => {
    mountCompare(buildCompare({ chargeSheet: { id: 'flat-10', confirmedOn: null } }));
    open();

    expect(await screen.findByText('Paper mean R (basis net, costs not confirmed by the owner)')).toBeInTheDocument();
  });
});
