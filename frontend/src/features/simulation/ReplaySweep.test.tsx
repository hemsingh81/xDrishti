import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { buildBasket, buildRiskProfiles } from '@/test/fixtures';
import { pinClock } from '@/test/pinClock';
import { renderWithProviders } from '@/test/render';
import { BASE, RUN, SWEEP, sweepRow, sweepView } from '@/test/replaySr02Fixtures';
import { problemResponse, server } from '@/test/server';
import { replayPollMs, sweepInterval } from './api/replayApi';
import { SweepCard } from './components/replay/SweepCard';
import { SweepStartForm } from './components/replay/SweepStartForm';
import { quarterStarts, sweepProblem, sweepStarts, initialSweepForm } from './lib/sweepForm';

pinClock('2026-11-12T04:00:00Z');

function form() {
  const posts: unknown[] = [];
  server.use(
    http.get('/api/baskets', () => HttpResponse.json([buildBasket()])),
    http.get('/api/risk-profiles', () => HttpResponse.json(buildRiskProfiles())),
    http.get('/api/charge-sheet/confirmation', () => problemResponse({ title: 'No sheet.' }, 404)),
    http.post('/api/replay/sweeps', async ({ request }) => {
      posts.push(await request.json());
      return HttpResponse.json({ runIds: [RUN, BASE], pairId: null, trialNumber: 1, sweepId: SWEEP }, { status: 202 });
    }),
  );
  const started: string[] = [];
  renderWithProviders(
    <SweepStartForm
      onStarted={(id) => {
        started.push(id);
      }}
    />,
  );
  return { posts, started };
}

describe('sweep form model', () => {
  it('lists every quarter from the first start and keeps a short month in range', () => {
    expect(quarterStarts('2025-01-31', 3)).toEqual(['2025-01-31', '2025-04-30', '2025-07-31']);
    expect(quarterStarts('2025-10-01', 2)).toEqual(['2025-10-01', '2026-01-01']);
  });

  it('refuses one date, thirteen dates and a word that is not a day', () => {
    const base = { ...initialSweepForm, mode: 'dates' as const };
    expect(sweepProblem({ ...base, dates: '2025-01-01' })).toMatch(/2 to 12/);
    const thirteen = quarterStarts('2022-01-01', 13).join(' ');
    expect(sweepProblem({ ...base, dates: thirteen })).toMatch(/2 to 12/);
    expect(sweepProblem({ ...base, dates: '2025-01-01, soon' })).toMatch(/"soon" is not a real day/);
    expect(sweepStarts({ ...base, dates: '2025-04-01 2025-01-01, 2025-01-01' })).toEqual(['2025-01-01', '2025-04-01']);
  });
});

describe('sweep start form', () => {
  it('says how many looks it adds, then posts the quarter dates', async () => {
    const { posts, started } = form();
    const user = userEvent.setup();
    await user.type(await screen.findByLabelText(/First start/), '2025-01-01');
    expect(screen.getByText(/This sweep adds 4 replay looks/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Start sweep' }));
    await waitFor(() => {
      expect(posts).toHaveLength(1);
    });
    expect(posts[0]).toMatchObject({
      starts: ['2025-01-01', '2025-04-01', '2025-07-01', '2025-10-01'],
      to: null,
      versions: 'start',
    });
    await waitFor(() => {
      expect(started).toEqual([SWEEP]);
    });
  });

  it('posts explicit dates', async () => {
    const { posts } = form();
    const user = userEvent.setup();
    await user.click(await screen.findByRole('combobox', { name: 'Start dates' }));
    await user.click(screen.getByRole('option', { name: 'Explicit dates' }));
    await user.type(screen.getByLabelText(/Dates \(yyyy-mm-dd\)/), '2025-02-03, 2025-05-05');
    expect(screen.getByText(/adds 2 replay looks/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Start sweep' }));
    await waitFor(() => {
      expect(posts[0]).toMatchObject({ starts: ['2025-02-03', '2025-05-05'] });
    });
  });

  it('sends nothing while the form has a problem', async () => {
    const { posts } = form();
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Start sweep' }));
    expect(screen.getByText('Choose the first start date.')).toBeInTheDocument();
    expect(posts).toHaveLength(0);
  });

  it('names each bad start of a 422 replay.invalid', async () => {
    form();
    server.use(
      http.post('/api/replay/sweeps', () =>
        problemResponse(
          {
            title: 'Refused',
            code: 'replay.invalid',
            detail: '2025-05-01 has fewer than 126 sessions before the end.',
          },
          422,
        ),
      ),
    );
    const user = userEvent.setup();
    await user.type(await screen.findByLabelText(/First start/), '2025-01-01');
    await user.click(screen.getByRole('button', { name: 'Start sweep' }));
    expect(
      await screen.findByText(/The sweep was not started\. 2025-05-01 has fewer than 126 sessions/),
    ).toBeInTheDocument();
  });

  it('says in plain words that a replay is running on a 409 replay.busy', async () => {
    form();
    server.use(
      http.post('/api/replay/sweeps', () =>
        problemResponse({ title: 'Busy', code: 'replay.busy', detail: 'A replay is already running.' }, 409),
      ),
    );
    const user = userEvent.setup();
    await user.type(await screen.findByLabelText(/First start/), '2025-01-01');
    await user.click(screen.getByRole('button', { name: 'Start sweep' }));
    expect(
      await screen.findByText(/A replay is already running\. Wait for it to finish or cancel it/),
    ).toBeInTheDocument();
  });
});

describe('sweep card', () => {
  const show = (view = sweepView()) => {
    server.use(http.get('/api/replay/sweeps/:id', () => HttpResponse.json(view)));
    renderWithProviders(<SweepCard sweepId={SWEEP} />);
  };

  it('shows the looks first, then the table and the spread of the completed starts', async () => {
    show();
    expect(await screen.findByText('This sweep adds 2 replay looks.')).toBeInTheDocument();
    const table = screen.getByRole('table', { name: 'Sweep starts' });
    expect(within(table).getAllByRole('row')).toHaveLength(3);
    expect(within(table).getByText('-4.10%')).toBeInTheDocument();
    expect(within(table).getByText('No')).toBeInTheDocument();
    expect(screen.getByText('min -4.10%, median 2.95%, max 10.00%')).toBeInTheDocument();
    expect(screen.getByText('min 8.00%, median 10.00%, max 12.00%')).toBeInTheDocument();
    expect(screen.getAllByText('50.00%')).toHaveLength(2);
    expect(screen.queryByRole('button', { name: 'Cancel sweep' })).not.toBeInTheDocument();
    expect(screen.queryByText(/Partial:/)).not.toBeInTheDocument();
  });

  it('shows a running sweep, with the progress of the start in flight, and cancels it', async () => {
    let cancels = 0;
    show(
      sweepView({
        state: 'running',
        complete: false,
        completedStarts: 1,
        starts: [
          sweepRow(),
          sweepRow({
            runId: BASE,
            from: '2025-04-01',
            state: 'running',
            sessionsDone: 30,
            sessionsTotal: 100,
            cagr: null,
            maxDrawdownPct: null,
            finalEquity: null,
            trades: null,
            beatsBenchmark: null,
          }),
        ],
      }),
    );
    server.use(
      http.post('/api/replay/sweeps/:id/cancel', () => {
        cancels += 1;
        return HttpResponse.json({ runsStopped: 2 }, { status: 202 });
      }),
    );
    const user = userEvent.setup();
    expect(await screen.findByText(/30 of 100 sessions/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cancel sweep' }));
    await waitFor(() => {
      expect(cancels).toBe(1);
    });
  });

  it('shows a cancelled sweep as partial', async () => {
    show(
      sweepView({
        state: 'cancelled',
        complete: false,
        completedStarts: 1,
        starts: [sweepRow(), sweepRow({ runId: BASE, from: '2025-04-01', state: 'cancelled', cagr: null })],
      }),
    );
    expect(await screen.findByText(/Cancelled: the sweep was stopped/)).toBeInTheDocument();
    expect(
      screen.getByText(/Partial: 1 of 2 starts completed\. The spread covers the completed starts only\./),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancel sweep' })).not.toBeInTheDocument();
  });

  it('shows a failed start with its reason', async () => {
    show(
      sweepView({
        state: 'failed',
        complete: false,
        completedStarts: 1,
        starts: [sweepRow(), sweepRow({ runId: BASE, state: 'failed', failReason: 'Bars are missing.', cagr: null })],
      }),
    );
    expect(await screen.findByText('Bars are missing.')).toBeInTheDocument();
    expect(screen.getByText(/Failed: at least one start failed/)).toBeInTheDocument();
  });

  it('shows an error when the sweep cannot be read', async () => {
    server.use(http.get('/api/replay/sweeps/:id', () => problemResponse({ title: 'Not found.' }, 404)));
    renderWithProviders(<SweepCard sweepId={SWEEP} />);
    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('reads again only while the sweep runs', () => {
    expect(sweepInterval({ state: 'running' })).toBe(replayPollMs);
    expect(sweepInterval({ state: 'completed' })).toBe(false);
    expect(sweepInterval(undefined)).toBe(false);
  });
});
