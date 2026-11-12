import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Schemas } from '@/shared/api';
import { buildBasket, buildRiskProfiles } from '@/test/fixtures';
import { pinClock } from '@/test/pinClock';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { pairInterval, replayPollMs, runInterval, runsInterval } from './api/replayApi';
import { SystemReplayPage } from './components/replay/SystemReplayPage';

pinClock('2026-11-12T04:00:00Z');

const RUN = '11111111-1111-1111-1111-111111111111';
const LEARN = '22222222-2222-2222-2222-222222222222';
const PAIR = '33333333-3333-3333-3333-333333333333';
const COSTS = 'before costs (charge sheet not filled, ISS-25)';

type Run = Schemas['ReplayRunView'];
type Item = Schemas['ReplayRunListItem'];

const completeness = (over: Partial<Schemas['ReplayCompleteness']> = {}) => ({
  complete: true,
  sessionsDone: 10,
  sessionsTotal: 10,
  asOfSession: '2024-01-12',
  ...over,
});

const item = (over: Partial<Item> = {}): Item => ({
  id: RUN,
  pairId: null,
  createdAt: '2026-11-12T03:00:00Z',
  from: '2024-01-01',
  to: '2024-01-12',
  mode: 'frozen',
  state: 'completed',
  completeness: completeness(),
  finalEquity: 1_050_000,
  maxDrawdownPct: 2,
  trialNumber: 7,
  costsLabel: COSTS,
  hindsight: [{ code: 'settings_now', text: 'Settings are as of today.' }],
  ...over,
});

const run = (over: Partial<Run> = {}): Run => ({
  hindsight: [
    { code: 'settings_now', text: 'Settings are as of today.' },
    { code: 'survivorship', text: 'Only stocks listed today.' },
  ],
  id: RUN,
  pairId: null,
  createdAt: '2026-11-12T03:00:00Z',
  finishedAt: '2026-11-12T03:05:00Z',
  from: '2024-01-01',
  to: '2024-01-12',
  mode: 'frozen',
  state: 'completed',
  failReason: null,
  completeness: completeness(),
  costsLabel: COSTS,
  basis: 'gross',
  trialNumber: 7,
  inputHash: 'a',
  resultHash: 'b',
  parameters: {},
  settingsSnapshot: {},
  figures: {
    capital: 1_000_000,
    finalEquity: 1_050_000,
    returnPct: 5,
    cagr: 0.12,
    maxDrawdownPct: 2,
    sharpe: 1.4,
    exposure: 0.5,
    trades: 3,
    closedTrades: 3,
    hitRate: 0.66,
    meanR: 0.4,
  },
  contribution: [],
  ...over,
});

const session = (over: Partial<Schemas['ReplaySessionListItem']> = {}): Schemas['ReplaySessionListItem'] => ({
  session: '2024-01-02',
  candidates: 5,
  taken: 2,
  blockedByReason: { sector_cap: 2 },
  pnl: 1200,
  equity: 1_001_200,
  drawdownPct: 0,
  openPositions: 1,
  labels: [{ code: 'survivorship', text: 'Only stocks listed today.' }],
  ...over,
});

const pairView = (over: Partial<Schemas['ReplayPairView']> = {}): Schemas['ReplayPairView'] => ({
  hindsight: [{ code: 'settings_now', text: 'Settings are as of today.' }],
  pairId: PAIR,
  frozen: item(),
  learning: item({ id: LEARN, mode: 'learning' }),
  answer: 'too_little_history',
  reason: null,
  days: 10,
  informativeDays: 3,
  meanDailyDifference: null,
  probabilityLearningBetter: 0.6,
  probabilityFrozenBetter: 0.4,
  low: -0.1,
  high: 0.2,
  cut: null,
  hurtsBar: null,
  minDays: 20,
  ...over,
});

interface Options {
  runs?: Item[];
  run?: Run | (() => Run);
  pair?: Schemas['ReplayPairView'];
}

function setup(options: Options = {}) {
  const counts = { run: 0, post: [] as unknown[], cancel: 0 };
  const current = options.run ?? run();
  server.use(
    http.get('/api/baskets', () => HttpResponse.json([buildBasket()])),
    http.get('/api/risk-profiles', () => HttpResponse.json(buildRiskProfiles())),
    http.get('/api/charge-sheet/confirmation', () => problemResponse({ title: 'No sheet.' }, 404)),
    http.get('/api/replay/runs', () => HttpResponse.json(options.runs ?? [item()])),
    http.get('/api/replay/runs/:id', () => {
      counts.run += 1;
      return HttpResponse.json(typeof current === 'function' ? current() : current);
    }),
    http.get('/api/replay/runs/:id/equity', () =>
      HttpResponse.json({
        runId: RUN,
        completeness: completeness(),
        costsLabel: COSTS,
        capital: 1_000_000,
        benchmarkSymbol: 'NIFTY 500',
        benchmarkReason: null,
        cashComplete: true,
        points: [
          { session: '2024-01-02', equity: 1_001_200, drawdownPct: 0, cash: 900_000, benchmark: 1_000_500 },
          { session: '2024-01-03', equity: 1_002_000, drawdownPct: 0.1, cash: 900_000, benchmark: 1_001_000 },
        ],
      }),
    ),
    http.get('/api/replay/runs/:id/sessions', () =>
      HttpResponse.json({ runId: RUN, completeness: completeness(), costsLabel: COSTS, sessions: [session()] }),
    ),
    http.get('/api/replay/runs/:id/sessions/:day', () =>
      HttpResponse.json({
        runId: RUN,
        session: '2024-01-02',
        costsLabel: COSTS,
        candidates: [{ symbol: 'AAA', side: 'BUY', cellKey: 'orb|5m', rank: 1 }],
        ranked: [],
        taken: [{ symbol: 'AAA', side: 'BUY', cellKey: 'orb|5m', rank: 1 }],
        blocked: [{ symbol: 'BBB', cellKey: 'orb|5m', reason: 'sector_cap', detail: 'Two in the sector already' }],
        fills: [
          {
            symbol: 'AAA',
            side: 'BUY',
            cellKey: 'orb|5m',
            session: '2024-01-03',
            status: 'filled',
            qty: 10,
            entryPrice: 100,
            detail: '',
          },
        ],
        exits: [],
        equity: 1_001_200,
        drawdownPct: 0,
        openRiskPct: 0.5,
        openPositions: 1,
        hindsight: [{ code: 'settings_now', text: 'Settings are as of today.' }],
        notes: [],
        openBookComplete: true,
        openBook: [
          {
            symbol: 'CCC',
            side: 'BUY',
            qty: 5,
            entrySession: '2024-01-01',
            entryPrice: 50,
            stopPrice: 48,
            swing: false,
          },
        ],
      }),
    ),
    http.get('/api/replay/runs/:id/trades', () =>
      HttpResponse.json({
        runId: RUN,
        completeness: completeness(),
        costsLabel: COSTS,
        trades: [
          {
            tradeNo: 1,
            symbol: 'AAA',
            side: 'BUY',
            strategy: 'orb',
            cellKey: 'k',
            qty: 10,
            entrySession: '2024-01-03',
            entryPrice: 100,
            stopPrice: 98,
            swing: false,
            exitSession: '2024-01-04',
            grossPnl: 50,
            charges: null,
            netPnl: null,
            r: 0.5,
          },
          {
            tradeNo: 2,
            symbol: 'ZZZ',
            side: 'SELL',
            strategy: 'orb',
            cellKey: 'k',
            qty: 10,
            entrySession: '2024-01-03',
            entryPrice: 10,
            stopPrice: 11,
            swing: false,
            exitSession: null,
            grossPnl: null,
            charges: null,
            netPnl: null,
            r: null,
          },
        ],
      }),
    ),
    http.get('/api/replay/pairs/:id', () => HttpResponse.json(options.pair ?? pairView())),
    http.post('/api/replay/runs', async ({ request }) => {
      counts.post.push(await request.json());
      return HttpResponse.json({ runIds: [RUN], pairId: null, trialNumber: 1 }, { status: 202 });
    }),
    http.post('/api/replay/runs/:id/cancel', () => {
      counts.cancel += 1;
      return HttpResponse.json({ state: 'cancelling' }, { status: 202 });
    }),
  );
  return counts;
}

const open = () => renderWithProviders(<SystemReplayPage />, { path: '/simulation/replay' });

describe('system replay screen', () => {
  it('says there is no replay yet', async () => {
    setup({ runs: [] });
    open();
    expect(await screen.findByText(/No replay yet/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'System replay' })).toHaveAttribute('aria-current', 'page');
  });

  it('shows an error when the runs cannot be read', async () => {
    setup();
    server.use(http.get('/api/replay/runs', () => problemResponse({ title: 'Down.' }, 500)));
    open();
    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('puts the hindsight banner first, with every label and the costs label, then the figures', async () => {
    setup();
    open();
    const banner = await screen.findByRole('note', { name: 'Hindsight labels' });
    expect(within(banner).getByText('Settings are as of today.')).toBeInTheDocument();
    expect(within(banner).getByText('Only stocks listed today.')).toBeInTheDocument();
    expect(within(banner).getByText(COSTS)).toBeInTheDocument();
    const tiles = await screen.findByRole('group', { name: 'Key figures' });
    expect(banner.compareDocumentPosition(tiles) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(within(tiles).getByText('12.00%')).toBeInTheDocument();
    // Exposure 0.5 and hit rate 0.66 are fractions: shown as percents.
    expect(within(tiles).getByText('50.00%')).toBeInTheDocument();
    expect(within(tiles).getByText('66.00%')).toBeInTheDocument();
    // The results come first; the form is folded away below them.
    const toggle = screen.getByRole('button', { name: 'New replay' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(banner.compareDocumentPosition(toggle) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Start replay' })).not.toBeInTheDocument();
    expect(within(tiles).getByText('The 7th replay of this range')).toBeInTheDocument();
  });

  it('opens the plan of a day with its blocked reasons', async () => {
    setup();
    const user = userEvent.setup();
    open();
    await user.click(await screen.findByRole('button', { name: /Open the plan of/ }));
    expect(await screen.findAllByText('Settings are as of today.')).toHaveLength(2);
    expect(await screen.findByText('BBB: sector_cap — Two in the sector already')).toBeInTheDocument();
    expect(screen.getByText(/CCC BUY/)).toBeInTheDocument();
  });

  it('shows the pair answer, even when it is too little history', async () => {
    setup({
      runs: [item({ pairId: PAIR }), item({ id: LEARN, pairId: PAIR, mode: 'learning' })],
      run: run({ pairId: PAIR }),
    });
    open();
    expect(await screen.findByRole('heading', { name: 'Frozen against learning' })).toBeInTheDocument();
    expect(await screen.findByText('Too little history to tell learning and frozen apart.')).toBeInTheDocument();
    expect(screen.getByText('60.00%')).toBeInTheDocument();
  });

  it('shows the pair interval and the mean daily difference as percents with three decimals', async () => {
    setup({
      runs: [item({ pairId: PAIR }), item({ id: LEARN, pairId: PAIR, mode: 'learning' })],
      run: run({ pairId: PAIR }),
      pair: pairView({
        answer: 'learning_better',
        meanDailyDifference: 0.0011,
        low: -0.00012,
        high: 0.00345,
      }),
    });
    open();
    expect(await screen.findByText('Learning did better than frozen on the informative days.')).toBeInTheDocument();
    expect(screen.getByText('-0.012% to 0.345%')).toBeInTheDocument();
    expect(screen.getByText('0.110%')).toBeInTheDocument();
  });

  it('shows no measurable difference as an ordinary answer', async () => {
    setup({
      runs: [item({ pairId: PAIR }), item({ id: LEARN, pairId: PAIR, mode: 'learning' })],
      run: run({ pairId: PAIR }),
      pair: pairView({ answer: 'no_measurable_difference' }),
    });
    open();
    expect(await screen.findByText('No measurable difference between learning and frozen.')).toBeInTheDocument();
  });

  it('shows no pair card for a single run', async () => {
    setup();
    open();
    await screen.findByRole('group', { name: 'Key figures' });
    expect(screen.queryByRole('heading', { name: 'Frozen against learning' })).not.toBeInTheDocument();
  });

  it('shows a running replay as partial and cancels it', async () => {
    let cancelled = false;
    const counts = setup({
      runs: [item({ state: 'running' })],
      run: () =>
        run({
          state: cancelled ? 'cancelling' : 'running',
          completeness: completeness({ complete: false, sessionsDone: 4 }),
        }),
    });
    server.use(
      http.post('/api/replay/runs/:id/cancel', () => {
        counts.cancel += 1;
        cancelled = true;
        return HttpResponse.json({ state: 'cancelling' }, { status: 202 });
      }),
    );
    const user = userEvent.setup();
    open();
    expect(await screen.findByText('4 of 10 sessions · as of 12 Jan 2024')).toBeInTheDocument();
    expect(screen.getByText(/Partial: 4 of 10/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(counts.cancel).toBe(1);
    });
    expect(await screen.findByText(/Cancelling after the session in flight/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
  });

  it('shows a cancelled replay and a failed one with the stored reason', async () => {
    setup({ runs: [item({ state: 'cancelled' })], run: run({ state: 'cancelled' }) });
    const first = open();
    expect(await screen.findByText(/Cancelled: the finished sessions are kept/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
    first.unmount();
    setup({
      runs: [item({ state: 'failed' })],
      run: run({
        state: 'failed',
        failReason: 'session 2024-01-05 had no bars',
        completeness: completeness({ complete: false }),
      }),
    });
    open();
    expect(await screen.findByText(/session 2024-01-05 had no bars/)).toBeInTheDocument();
  });

  it('reads again only while something moves', () => {
    expect(runInterval({ state: 'queued' })).toBe(replayPollMs);
    expect(runInterval({ state: 'running' })).toBe(replayPollMs);
    expect(runInterval({ state: 'cancelling' })).toBe(replayPollMs);
    for (const state of ['completed', 'cancelled', 'failed']) expect(runInterval({ state })).toBe(false);
    expect(runInterval(undefined)).toBe(false);
    expect(runsInterval([{ state: 'completed' }, { state: 'running' }])).toBe(replayPollMs);
    expect(runsInterval([{ state: 'completed' }])).toBe(false);
    expect(runsInterval(undefined)).toBe(false);
  });

  it('reads the pair again only while it has no answer and a run moves', () => {
    const view = (answer: string | null, frozen: string, learning: string) => ({
      answer,
      frozen: { state: frozen },
      learning: { state: learning },
    });
    expect(pairInterval(view(null, 'completed', 'running'))).toBe(replayPollMs);
    expect(pairInterval(view(null, 'running', 'completed'))).toBe(replayPollMs);
    expect(pairInterval(view(null, 'completed', 'completed'))).toBe(false);
    expect(pairInterval(view('learning_better', 'completed', 'running'))).toBe(false);
    expect(pairInterval(undefined)).toBe(false);
  });

  it('reads the detail once more when the run leaves the running state', async () => {
    let state = 'running';
    const counts = setup({ runs: [item({ state: 'running' })], run: () => run({ state }) });
    const view = open();
    await screen.findByText(/sessions/);
    let equityReads = 0;
    server.use(
      http.get('/api/replay/runs/:id/equity', () => {
        equityReads += 1;
        return HttpResponse.json({
          hindsight: [],
          runId: RUN,
          completeness: completeness(),
          costsLabel: COSTS,
          capital: 1_000_000,
          benchmarkSymbol: null,
          benchmarkReason: null,
          cashComplete: true,
          points: [],
        });
      }),
    );
    state = 'completed';
    await view.client.invalidateQueries({ queryKey: ['simulation', 'replay', 'run'] });
    await waitFor(() => {
      expect(equityReads).toBeGreaterThan(0);
    });
    expect(counts.run).toBeGreaterThan(1);
  });

  describe('start form', () => {
    it('refuses a missing start date before anything is sent', async () => {
      const counts = setup({ runs: [] });
      const user = userEvent.setup();
      open();
      await user.click(await screen.findByRole('button', { name: 'Start replay' }));
      expect(screen.getByText('Choose the start date.')).toBeInTheDocument();
      expect(counts.post).toHaveLength(0);
    });

    it('sends the defaults and both checkboxes off', async () => {
      const counts = setup({ runs: [] });
      const user = userEvent.setup();
      open();
      await user.type(await screen.findByLabelText(/From/), '2024-01-01');
      expect(screen.getByLabelText(/Skip event \/ F&O-ban/)).not.toBeChecked();
      expect(screen.getByLabelText(/Let research-stage cells trade/)).not.toBeChecked();
      expect(screen.getByText(/Adds a hindsight label: filters/)).toBeInTheDocument();
      await user.click(screen.getByRole('radio', { name: 'Both (compared)' }));
      await user.click(screen.getByRole('button', { name: 'Start replay' }));
      await waitFor(() => {
        expect(counts.post).toHaveLength(1);
      });
      expect(counts.post[0]).toMatchObject({
        from: '2024-01-01',
        to: null,
        capital: 1000000,
        costs: 'gross',
        versions: 'start',
        modes: ['frozen', 'learning'],
        filtersWithoutHistory: false,
        includeResearchCells: false,
      });
    });

    it('goes to the started run when the API answers 202', async () => {
      setup({ runs: [] });
      const user = userEvent.setup();
      const view = open();
      await user.type(await screen.findByLabelText(/From/), '2024-01-01');
      await user.click(screen.getByRole('button', { name: 'Start replay' }));
      await waitFor(() => {
        expect(view.router.state.location.search).toBe(`?run=${RUN}`);
      });
    });

    it("offers only the two versions rules, today's active versions first", async () => {
      setup({ runs: [] });
      const user = userEvent.setup();
      open();
      await user.click(await screen.findByRole('combobox', { name: 'Versions rule' }));
      const options = screen.getAllByRole('option').map((o) => o.textContent);
      expect(options).toEqual(["Today's active versions (hindsight, default)", 'As of each day']);
    });

    it.each([
      [422, 'replay.invalid', 'The scope is too large; the largest allowed scope is 300 stocks.'],
      [409, 'replay.busy', 'A replay is already running.'],
      [409, 'replay.holdout', 'The range reaches the hold-out; the last allowed date is 2025-06-30.'],
    ])('shows the %i refusal %s inline', async (status, code, detail) => {
      setup({ runs: [] });
      server.use(http.post('/api/replay/runs', () => problemResponse({ title: 'Refused', code, detail }, status)));
      const user = userEvent.setup();
      open();
      await user.type(await screen.findByLabelText(/From/), '2024-01-01');
      await user.click(screen.getByRole('button', { name: 'Start replay' }));
      expect(await screen.findByText(detail)).toBeInTheDocument();
    });
  });
});
