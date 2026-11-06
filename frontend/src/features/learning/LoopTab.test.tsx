import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { buildFullLoopReport, buildLoopReport, mountConfidence } from '@/test/confidenceFixtures';
import { mountLearning } from '@/test/learningFixtures';
import { server } from '@/test/server';
import { renderLearning } from './testing';

afterEach(() => {
  server.events.removeAllListeners();
});

describe('Loop tab', () => {
  it('says "Not available" with the reason for every section when nothing has a source (today)', async () => {
    mountLearning();
    const seen = mountConfidence();
    renderLearning('?tab=loop');

    expect(await screen.findByText(/no stored scores to replay the arms on/)).toBeInTheDocument();
    expect(screen.getByText(/the learning-curve refit loop is not built/)).toBeInTheDocument();
    expect(screen.getAllByText(/no stored scores$/)).toHaveLength(2);
    expect(screen.getAllByText('Not available')).toHaveLength(4);
    // No table of figures, no zero, no pass word.
    const panel = screen.getByRole('tabpanel');
    expect(within(panel).queryAllByRole('table')).toHaveLength(0);
    expect(within(panel).queryByText(/\b0\.00\b/)).not.toBeInTheDocument();
    expect(within(panel).queryByText(/^(MET|Helps|Hurts)$/)).not.toBeInTheDocument();
    expect(screen.getByText('Loop: nothing can be judged yet.')).toBeInTheDocument();
    expect(screen.getByText('Nothing here is a forecast.')).toBeInTheDocument();
    expect(seen.methods.every((method) => method === 'GET')).toBe(true);
  });

  it('shows the arms, the interval, the curve, the slope and the lift when they exist', async () => {
    mountLearning();
    mountConfidence({ loop: buildFullLoopReport() });
    renderLearning('?tab=loop');

    const arms = await screen.findByRole('table', { name: 'Mean value per candidate by arm' });
    expect(within(arms).getByText('A: frozen model')).toBeInTheDocument();
    expect(within(arms).getByText('+0.05')).toBeInTheDocument();

    const comparisons = screen.getByRole('table', { name: 'Arm comparisons' });
    const rows = within(comparisons).getAllByRole('row');
    expect(rows[1]).toHaveTextContent('Helps');
    expect(rows[1]).toHaveTextContent('+0.03');
    expect(rows[1]).toHaveTextContent('+0.005');
    expect(rows[1]).toHaveTextContent('+0.055');
    expect(rows[1]).toHaveTextContent('97.0%');
    // An inconclusive comparison is not a pass and draws no number.
    expect(rows[2]).toHaveTextContent('Inconclusive (fewer days than the minimum; not a pass)');
    expect(rows[2]).toHaveTextContent('Not available');

    const curve = screen.getByRole('region', { name: 'Learning curve' });
    expect(curve).toHaveTextContent('Unclear (a projection, not a finding)');
    expect(curve).toHaveTextContent('no standard error with only three points');
    expect(curve).toHaveTextContent('+0.0045');
    expect(curve).toHaveTextContent('204');
    expect(curve).toHaveTextContent('54');

    const slope = screen.getByRole('region', { name: 'Reliability slope' });
    expect(slope).toHaveTextContent('Alarm: outside the band');
    expect(slope).toHaveTextContent('220');
    expect(slope).toHaveTextContent('0.45');

    const lift = screen.getByRole('region', { name: 'Decision-quality lift' });
    expect(lift).toHaveTextContent('+0.30');
    expect(lift).toHaveTextContent('0.79');
    expect(lift).toHaveTextContent('21 of 30 with a stored score');
    expect(lift).toHaveTextContent('2.19');
  });

  it('keeps a row for a comparison the server did not compute', async () => {
    const full = buildFullLoopReport();
    mountLearning();
    mountConfidence({ loop: { ...full, arms: { ...full.arms, retrainedVsNoMl: null } } });
    renderLearning('?tab=loop');

    const rows = within(await screen.findByRole('table', { name: 'Arm comparisons' })).getAllByRole('row');
    expect(rows).toHaveLength(3);
    expect(rows[1]).toHaveTextContent('Helps');
    expect(rows[2]).toHaveTextContent('Retrained against no model');
    expect(rows[2]).toHaveTextContent('Not available (not computed)');
    // No zero stands in for a missing candidate count.
    expect(rows[2]).not.toHaveTextContent(/\b0\b/);
  });

  it('keeps an available section beside an unavailable one honest', async () => {
    mountLearning();
    mountConfidence({ loop: { ...buildLoopReport(), slope: buildFullLoopReport().slope } });
    renderLearning('?tab=loop');

    expect(await screen.findByRole('region', { name: 'Reliability slope' })).toHaveTextContent(
      'Alarm: outside the band',
    );
    expect(screen.getByRole('region', { name: 'Learning curve' })).toHaveTextContent('Not available');
    expect(screen.getByRole('region', { name: 'The three arms' })).toHaveTextContent('Not available');
  });

  it('shows the error when the report fails', async () => {
    mountLearning();
    mountConfidence();
    server.use(http.get('/api/confidence/loop', () => HttpResponse.json({ title: 'Loop down.' }, { status: 500 })));
    renderLearning('?tab=loop');

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('has no control that changes anything', async () => {
    mountLearning();
    mountConfidence({ loop: buildFullLoopReport() });
    renderLearning('?tab=loop');

    await screen.findByRole('table', { name: 'Arm comparisons' });
    const panel = screen.getByRole('tabpanel');
    expect(within(panel).queryAllByRole('button')).toHaveLength(0);
    expect(within(panel).queryAllByRole('switch')).toHaveLength(0);
  });
});
