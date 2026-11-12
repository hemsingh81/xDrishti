import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { buildBasket, buildRiskProfiles } from '@/test/fixtures';
import { pinClock } from '@/test/pinClock';
import { renderWithProviders } from '@/test/render';
import { armComparison, baselinePair, BASE, COSTS, PAIR, RUN, runItem } from '@/test/replaySr02Fixtures';
import { problemResponse, server } from '@/test/server';
import { pairInterval, replayPollMs } from './api/replayApi';
import { PairCard } from './components/replay/PairCard';
import { ReplayCharts } from './components/replay/ReplayCharts';
import { ReplayStartForm } from './components/replay/ReplayStartForm';

pinClock('2026-11-12T04:00:00Z');

describe('the no-selection baseline', () => {
  describe('start form', () => {
    function form() {
      const posts: unknown[] = [];
      server.use(
        http.get('/api/baskets', () => HttpResponse.json([buildBasket()])),
        http.get('/api/risk-profiles', () => HttpResponse.json(buildRiskProfiles())),
        http.get('/api/charge-sheet/confirmation', () => problemResponse({ title: 'No sheet.' }, 404)),
        http.post('/api/replay/runs', async ({ request }) => {
          posts.push(await request.json());
          return HttpResponse.json({ runIds: [RUN, BASE], pairId: PAIR, trialNumber: 1 }, { status: 202 });
        }),
      );
      renderWithProviders(<ReplayStartForm onStarted={() => undefined} />);
      return posts;
    }

    it('sends baseline false by default and true when the toggle is on', async () => {
      const posts = form();
      const user = userEvent.setup();
      await user.type(await screen.findByLabelText(/From/), '2024-01-01');
      expect(screen.getByLabelText('With no-selection baseline')).not.toBeChecked();
      await user.click(screen.getByRole('button', { name: 'Start replay' }));
      await waitFor(() => {
        expect(posts).toHaveLength(1);
      });
      expect(posts[0]).toMatchObject({ baseline: false });
      await user.click(screen.getByLabelText('With no-selection baseline'));
      expect(screen.getByText(/It is not random stocks/)).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Start replay' }));
      await waitFor(() => {
        expect(posts).toHaveLength(2);
      });
      expect(posts[1]).toMatchObject({ baseline: true, modes: ['frozen'] });
    });

    it('offers the toggle in frozen mode only', async () => {
      form();
      const user = userEvent.setup();
      await screen.findByLabelText(/From/);
      await user.click(screen.getByRole('radio', { name: 'Learning' }));
      expect(screen.queryByLabelText('With no-selection baseline')).not.toBeInTheDocument();
      await user.click(screen.getByRole('radio', { name: 'Both (compared)' }));
      expect(screen.queryByLabelText('With no-selection baseline')).not.toBeInTheDocument();
    });
  });

  describe('arm comparison card', () => {
    const show = (pair = baselinePair()) => {
      server.use(http.get('/api/replay/pairs/:id', () => HttpResponse.json(pair)));
      renderWithProviders(<PairCard pairId={PAIR} />);
    };

    it('shows the hindsight labels first, then what the baseline is, the shortcut, n, P and the answer', async () => {
      show();
      const heading = await screen.findByRole('heading', { name: 'Selection against the no-selection baseline' });
      const card = heading.closest('section');
      expect(card).not.toBeNull();
      const text = card?.textContent ?? '';
      expect(text.indexOf('Settings are as of today.')).toBeLessThan(text.indexOf('not random stocks'));
      expect(text.indexOf('not random stocks')).toBeLessThan(text.indexOf('Shortcut:'));
      expect(screen.getByText(/Equity is realised-only/)).toBeInTheDocument();
      expect(screen.getByText('140')).toBeInTheDocument();
      expect(screen.getByText('97.00%')).toBeInTheDocument();
      expect(screen.getByText(/system's choosing did better than taking every candidate/)).toBeInTheDocument();
    });

    it.each([
      ['baseline_better', /Taking every candidate did better/],
      ['no_measurable_difference', /No measurable difference/],
      ['too_little_history', /Too little history/],
    ])('shows %s in plain words', async (answer, words) => {
      show(baselinePair({ selectionVsBaseline: armComparison({ answer, informativeDays: 15 }) }));
      expect(await screen.findByText(words)).toBeInTheDocument();
      expect(screen.getByText('15')).toBeInTheDocument();
    });

    it('shows the reason when there is no answer yet', async () => {
      show(
        baselinePair({
          selectionVsBaseline: armComparison({
            answer: null,
            reason: 'A run is not complete.',
            informativeDays: null,
            probabilitySelectionBetter: null,
            probabilityBaselineBetter: null,
          }),
        }),
      );
      expect(await screen.findByText('A run is not complete.')).toBeInTheDocument();
      expect(screen.getAllByText('n/a').length).toBeGreaterThan(0);
    });

    it('still shows the frozen against learning card for an ordinary pair', async () => {
      show(
        baselinePair({
          selectionVsBaseline: null,
          answer: 'no_measurable_difference',
          frozen: runItem(),
          learning: runItem(),
        }),
      );
      expect(await screen.findByRole('heading', { name: 'Frozen against learning' })).toBeInTheDocument();
    });
  });

  it('reads a baseline pair again only while it has no answer and an arm moves', () => {
    const arms = (selection: string, answer: string | null) => ({
      answer: null,
      frozen: null,
      learning: null,
      selectionVsBaseline: { answer, selection: { state: selection }, baseline: { state: 'completed' } },
    });
    expect(pairInterval(arms('running', null))).toBe(replayPollMs);
    expect(pairInterval(arms('completed', 'selection_better'))).toBe(false);
    expect(pairInterval(arms('completed', null))).toBe(false);
  });

  it('draws the other arm as a line on the equity chart', async () => {
    server.use(
      http.get('/api/replay/runs/:id/equity', ({ params }) =>
        HttpResponse.json({
          runId: params.id,
          completeness: { complete: true, sessionsDone: 1, sessionsTotal: 1, asOfSession: '2024-01-02' },
          costsLabel: COSTS,
          capital: 1_000_000,
          benchmarkSymbol: 'NIFTY 500',
          benchmarkReason: null,
          cashComplete: true,
          points: [
            {
              session: '2024-01-02',
              equity: params.id === BASE ? 1_002_000 : 1_001_000,
              drawdownPct: 0,
              cash: 900_000,
              benchmark: 1_000_500,
            },
          ],
        }),
      ),
    );
    renderWithProviders(
      <ReplayCharts runId={RUN} active={false} other={{ id: BASE, name: 'Baseline (no selection)' }} />,
    );
    expect(await screen.findByRole('button', { name: 'Baseline (no selection)' })).toBeInTheDocument();
  });
});
