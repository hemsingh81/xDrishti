import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Schemas } from '@/shared/api';
import { buildBasket } from '@/test/fixtures';
import { pinClock } from '@/test/pinClock';
import { buildEquity, buildEvidence, buildGroup } from '@/test/reportFixtures';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { SimulationReportPage } from './components/SimulationReportPage';

pinClock('2026-11-10T04:00:00Z');

type Completeness = Schemas['TrainingCompletenessView'];

const completeness = (over: Partial<Completeness> = {}): Completeness => ({
  kind: 'training_simulation',
  label: 'Training simulation, not evidence.',
  simJob: '11111111-1111-1111-1111-111111111111',
  jobState: 'running',
  complete: false,
  chunksDone: 30,
  chunksTotal: 100,
  chunksFailed: 1,
  chunksRunning: 2,
  chunksPending: 67,
  asOf: '2026-11-10T03:59:00Z',
  windowFrom: '2018-01-01',
  windowTo: '2025-12-31',
  holdOutStart: '2026-01-01',
  rangeFrom: '2024-01-01',
  rangeTo: '2025-12-31',
  rangeCut: true,
  locked: 'The part of the range from 2026-01-01 is the locked hold-out and was not read.',
  symbolsWithoutResults: [],
  trainingRecord: '/api/simulate-all/11111111-1111-1111-1111-111111111111/training',
  ...over,
});

const job = (over: Partial<Schemas['SimAllView']> = {}): Schemas['SimAllView'] => ({
  id: '11111111-1111-1111-1111-111111111111',
  state: 'running',
  pauseReason: null,
  windowFrom: '2018-01-01',
  windowTo: '2025-12-31',
  holdOutStart: '2026-01-01',
  coveredFrom: '2018-01-01',
  coveredTo: '2022-01-01',
  chunksTotal: 100,
  chunksDone: 30,
  chunksFailed: 1,
  chunksRunning: 2,
  chunksRemaining: 67,
  workers: 2,
  startedBy: 'owner',
  startedAt: '2026-11-10T01:00:00Z',
  updatedAt: '2026-11-10T03:59:00Z',
  finishedAt: null,
  estimate: {
    sampledChunks: 30,
    averageChunkMilliseconds: 1000,
    remainingSeconds: 7200,
    averageStoredBytes: 1_000_000,
    remainingStoredBytes: 67_000_000,
    basis: 'measured',
  },
  ...over,
});

function setup(options: { job?: Schemas['SimAllView'] | null; completeness?: Completeness } = {}) {
  const requests: URL[] = [];
  const current = options.job === undefined ? job() : options.job;
  server.use(
    http.get('/api/simulate-all/current', () =>
      current === null
        ? problemResponse({ title: 'No job.', code: 'simulate_all.no_active_job' }, 404)
        : HttpResponse.json(current),
    ),
    http.get('/api/baskets', () => HttpResponse.json([buildBasket()])),
    http.get('/api/reports/summary', ({ request }) => {
      requests.push(new URL(request.url));
      return HttpResponse.json({
        all: buildGroup(),
        evidence: buildEvidence({ completeness: options.completeness ?? completeness() }),
      });
    }),
    http.get('/api/reports/equity', ({ request }) => {
      requests.push(new URL(request.url));
      return HttpResponse.json(buildEquity());
    }),
  );
  return requests;
}

const open = () => renderWithProviders(<SimulationReportPage />, { path: '/reports/simulation' });

describe('simulation report screen', () => {
  it('says on the page that these are training simulations, not evidence', async () => {
    setup();
    open();
    expect(await screen.findByText(/Training simulations, not evidence/)).toBeInTheDocument();
  });

  it('shows the key figures of the chosen stock and range, asking for the training purpose', async () => {
    const requests = setup();
    const user = userEvent.setup();
    open();
    await screen.findByRole('heading', { name: 'Choose a report' });
    await user.click(screen.getByRole('button', { name: 'Stock' }));
    await user.type(screen.getByLabelText('Stock symbol'), 'reliance');
    await user.type(screen.getByLabelText('From'), '2024-01-01');
    await screen.findByRole('heading', { name: 'Key figures' });
    await waitFor(() => {
      const last = requests[requests.length - 1];
      expect(last?.searchParams.get('purpose')).toBe('training');
      expect(last?.searchParams.get('symbol')).toBe('RELIANCE');
      expect(last?.searchParams.get('from')).toBe('2024-01-01');
    });
  });

  it('reads a basket and another report type', async () => {
    const requests = setup();
    const user = userEvent.setup();
    open();
    await screen.findByRole('heading', { name: 'Choose a report' });
    await user.click(screen.getByRole('button', { name: 'Basket' }));
    await user.click(screen.getByRole('combobox', { name: 'Basket' }));
    await user.click(await screen.findByRole('option', { name: 'MyChoice' }));
    await user.click(screen.getByRole('combobox', { name: 'Report' }));
    await user.click(await screen.findByRole('option', { name: 'Equity and drawdown' }));
    await screen.findByRole('heading', { name: 'Equity and drawdown' });
    await waitFor(() => {
      expect(requests.some((url) => url.pathname.endsWith('/equity') && url.searchParams.has('basket'))).toBe(true);
    });
  });

  it('shows a partial banner and the locked hold-out, and refreshes only when asked', async () => {
    const requests = setup();
    const user = userEvent.setup();
    open();
    const banner = await screen.findByRole('region', { name: 'How complete is this data' });
    expect(within(banner).getByText(/Partial: 30 of 100 chunks/)).toBeInTheDocument();
    expect(within(banner).getByRole('progressbar', { name: 'Report data complete' })).toHaveAttribute(
      'aria-valuenow',
      '30',
    );
    expect(within(banner).getByText(/Locked: the hold-out block from .* is never read/)).toBeInTheDocument();
    expect(within(banner).getByText(/locked hold-out and was not read/)).toBeInTheDocument();
    const before = requests.length;
    await user.click(screen.getByRole('button', { name: 'Refresh report' }));
    await waitFor(() => {
      expect(requests.length).toBeGreaterThan(before);
    });
  });

  it('does not refetch the report by itself', async () => {
    const requests = setup();
    open();
    await screen.findByRole('region', { name: 'How complete is this data' });
    const seen = requests.length;
    await new Promise((resolve) => setTimeout(resolve, 100));
    expect(requests.length).toBe(seen);
  });

  it('names stocks with no result', async () => {
    setup({ completeness: completeness({ complete: true, jobState: 'completed', symbolsWithoutResults: ['TCS'] }) });
    open();
    expect(await screen.findByText('No trade yet for: TCS.')).toBeInTheDocument();
    expect(screen.getByText(/Complete: every chunk/)).toBeInTheDocument();
  });

  it('shows the progress and the pre-run estimate, and pauses the job', async () => {
    setup();
    let paused = false;
    server.use(
      http.post('/api/simulate-all/pause', () => {
        paused = true;
        server.use(http.get('/api/simulate-all/current', () => HttpResponse.json(job({ state: 'paused' }))));
        return HttpResponse.json(job({ state: 'paused' }));
      }),
    );
    const user = userEvent.setup();
    open();
    expect(await screen.findByText(/30 of 100 chunks done/)).toBeInTheDocument();
    expect(screen.getByText(/about 2h .* left and 67\.0 MB more to store/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Pause' }));
    await waitFor(() => {
      expect(paused).toBe(true);
    });
    expect(await screen.findByText(/State: paused/)).toBeInTheDocument();
  });

  it('offers resume for a paused job and cancel for an active one', async () => {
    setup({ job: job({ state: 'paused', pauseReason: 'market_hours', estimate: null }) });
    open();
    expect(await screen.findByRole('button', { name: 'Resume' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    expect(screen.getByText(/State: paused \(market hours\)/)).toBeInTheDocument();
    expect(screen.getByText(/measured after 1 % of the chunks/)).toBeInTheDocument();
  });

  it('starts a job from the empty state and shows a refusal', async () => {
    setup({ job: null });
    server.use(
      http.post('/api/simulate-all', () =>
        problemResponse(
          { title: 'The history backfill is not complete.', code: 'simulate_all.backfill_incomplete' },
          422,
        ),
      ),
    );
    const user = userEvent.setup();
    open();
    expect(await screen.findByText(/No simulation has run yet/)).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Choose a report' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Start simulation' }));
    expect(await screen.findByText('The history backfill is not complete.')).toBeInTheDocument();
  });

  it('refuses a bad range before asking', async () => {
    const requests = setup();
    const user = userEvent.setup();
    open();
    await screen.findByRole('heading', { name: 'Choose a report' });
    await user.type(screen.getByLabelText('From'), '2025-06-01');
    await user.type(screen.getByLabelText('To'), '2025-01-01');
    expect(await screen.findByText('The from date is after the to date.')).toBeInTheDocument();
    const count = requests.length;
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(requests.length).toBe(count);
  });

  it('shows an error when the job cannot be read', async () => {
    server.use(http.get('/api/simulate-all/current', () => problemResponse({ title: 'Boom.', code: 'x' }, 500)));
    open();
    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('shows a loading state first', () => {
    setup();
    open();
    expect(screen.getByText(/Loading the simulation job/)).toBeInTheDocument();
  });

  it('shows an error in the report panel when the report is refused', async () => {
    setup();
    server.use(
      http.get('/api/reports/summary', () =>
        problemResponse({ title: 'The answer is too large.', code: 'reports.too_large' }, 422),
      ),
    );
    open();
    expect(await screen.findAllByText('The answer is too large.')).not.toHaveLength(0);
  });
});
