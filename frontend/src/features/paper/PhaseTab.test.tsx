import { screen, within } from '@testing-library/react';
import { buildPhase, mountPhase } from '@/test/phaseFixtures';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { PaperPage } from './components/PaperPage';

pinClock('2026-11-20T18:00:00+05:30');

const open = () => renderRoutes([{ path: '/paper', element: <PaperPage /> }], { initialPath: '/paper?tab=phase' });

describe('Paper phase check tab', () => {
  it('shows the three verdict words, the facts, the blockers and the limits, and offers no write control', async () => {
    mountPhase();
    open();

    expect(await screen.findByRole('heading', { name: 'The P10 exit' })).toBeInTheDocument();
    expect(screen.getAllByText('NOT EVALUABLE').length).toBeGreaterThanOrEqual(5);
    expect(screen.getAllByText('MET').length).toBe(1);
    expect(screen.getByText('NOT EVALUABLE: day 19 of 84 (week 3 of 12)')).toBeInTheDocument();
    expect(
      within(screen.getByRole('list', { name: 'Facts of C1' })).getByText('19 elapsed days of 84'),
    ).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Blockers of C3' })).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Limits' })).toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'Weeks of the epoch' })).toBeInTheDocument();
    expect(screen.getByText('2 (judged)')).toBeInTheDocument();
    expect(screen.getByText('1 (not judged)')).toBeInTheDocument();
    expect(screen.getByText(/Service paper-capture: paused/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /approve|apply|start|confirm|save|run/i })).not.toBeInTheDocument();
  });

  it('prints a met exit with its caveats in the headline and NOT MET in words', async () => {
    const label =
      'MET (point estimate; 90% interval [-0.12, +0.51] includes 0; 2 risk-down changes: the population was censored)';
    const base = buildPhase();
    mountPhase(
      buildPhase({
        exitVerdict: 'MET',
        exitCriterion: label,
        criteria: base.report.criteria.map((c) =>
          c.id === 'C1' ? { ...c, verdict: 'NOT MET', label: 'NOT MET: coverage 0.72' } : c,
        ),
      }),
    );
    open();

    expect(await screen.findByText(label)).toBeInTheDocument();
    expect(screen.getByText('NOT MET: coverage 0.72')).toBeInTheDocument();
    expect(screen.getByText('NOT MET')).toBeInTheDocument();
  });

  it('says there is no epoch yet', async () => {
    mountPhase(buildPhase({ epoch: null, epochs: [], weeks: [], elapsedDays: 0 }));
    open();

    expect(await screen.findByText(/No paper epoch yet/)).toBeInTheDocument();
  });
});
