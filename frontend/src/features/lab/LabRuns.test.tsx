import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { createHubFake } from '@/test/hubFake';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { LabPage } from './components/LabPage';

type Run = Schemas['LabRunView'];
type Handler = ReturnType<typeof http.get>;
const runId = '00000000-0000-4000-8000-0000000000a1';
const sweepId = '00000000-0000-4000-8000-0000000000ff';
const hash = 'abcdef0123456789abcdef';
const window = { from: '2025-10-01', to: '2026-10-01' };

const buildRun = (over: Partial<Run> = {}): Run => ({
  id: runId,
  key: 'k',
  state: 'running',
  stage: 'quick_test',
  strategy: { id: 'MY_ORB', version: 2, hash },
  universe: {
    kind: 'symbols',
    basketId: null,
    label: '2 symbols',
    requested: 2,
    instruments: [],
    sampled: false,
    seed: null,
  },
  window,
  method: 'quick',
  progress: { done: 0, total: 0 },
  simRunKeys: [],
  counts: null,
  problems: [],
  stages: [],
  labels: ['Quick test: exploration, not a verdict.'],
  error: null,
  cancelRequested: false,
  createdAt: '2026-10-20T04:00:00Z',
  startedAt: '2026-10-20T04:00:01Z',
  finishedAt: null,
  verdict: null,
  ...over,
});

const verdict = {
  outcome: 'MARGINAL',
  variantCount: 4,
  adjustmentFactor: 1.2,
  evidence: {} as Schemas['EvidenceView'],
  gates: [
    { name: 'oos_trades', required: 100, adjusted: 120, actual: 130, passed: true, reason: '130 trades against 120' },
    {
      name: 'expectancy_r',
      required: 0.1,
      adjusted: 0.12,
      actual: 0.05,
      passed: false,
      reason: '0.05 R against 0.12 R',
    },
  ],
  limits: ['About 2.1 years of data, no hold-out.'],
  cells: [],
  fullPeriod: [],
  labels: [],
} as Schemas['LabVerdictView'];

const report = {
  runId,
  outcome: 'MARGINAL',
  variantCount: 4,
  adjustmentFactor: 1.2,
  run: {} as Schemas['ReportRunView'],
  kpis: { oosTrades: 130, expectancyR: 0.05, profitFactor: 1.1, maxDrawdownR: 4, folds: 5, positiveFolds: 3 },
  gates: [],
  bestCells: [],
  exploration: [],
  robustness: null,
  warnings: [{ code: 'cost_sensitivity', text: 'A small rise in costs erases the edge.' }],
  honestLimits: ['Full-period figures are in-sample exploration.'],
  notIncluded: ['Regime breakdown'],
  labels: [],
  summary: 'Marginal: expectancy is below the adjusted gate.',
} as Schemas['LabReportView'];

const ledgerRow = (id: string, method: string) => ({
  runId: id,
  runKey: id,
  strategyHash: hash,
  parentHash: null,
  version: 2,
  universe: '2 symbols',
  window,
  method,
  createdAt: '2026-10-20T04:00:00Z',
});

function setup(runs: Run[], extra: Handler[] = []) {
  server.use(
    ...extra,
    http.get('/api/lab/schema', () => HttpResponse.text('{"type":"object"}')),
    http.get('/api/lab/strategies', () =>
      HttpResponse.json([
        {
          id: 'MY_ORB',
          style: 'intraday',
          builtIn: false,
          createdAt: '2026-10-02T00:00:00Z',
          latestVersion: 2,
          versionCount: 2,
        },
      ]),
    ),
    http.post('/api/lab/validate', () =>
      HttpResponse.json({
        valid: true,
        hash,
        id: 'MY_ORB',
        style: 'intraday',
        version: 1,
        timeframes: [],
        sides: [],
        problems: [],
      }),
    ),
    http.get('/api/lab/strategies/:id', ({ params }) =>
      HttpResponse.json({
        id: params.id,
        style: 'intraday',
        builtIn: false,
        createdAt: '2026-10-02T00:00:00Z',
        latestVersion: 2,
        versions: [{ version: 2, hash, parentHash: null, note: null, createdAt: '2026-10-03T00:00:00Z' }],
      }),
    ),
    http.get('/api/lab/strategies/:id/versions/:version', ({ params }) =>
      HttpResponse.json({
        id: params.id,
        version: 2,
        hash,
        parentHash: null,
        note: null,
        createdAt: '2026-10-03T00:00:00Z',
        text: 'x: 1',
        problemsFree: true,
      }),
    ),
    http.get('/api/lab/runs', () => HttpResponse.json(runs)),
    http.get('/api/baskets', () => HttpResponse.json([])),
    http.get('/api/lab/strategies/:id/ledger', ({ params }) =>
      HttpResponse.json({ strategyId: params.id, variantCount: 0, adjustmentFactor: 1, rows: [] }),
    ),
  );
}

async function openFamily(user: ReturnType<typeof userEvent.setup>, version = true) {
  await user.click(await screen.findByRole('button', { name: /^MY_ORB\s*intraday/ }));
  if (version) await user.click(await screen.findByRole('button', { name: /Version 2/ }));
  if (version) {
    await waitFor(() => {
      expect(screen.getByLabelText('Strategy text')).toHaveValue('x: 1');
    });
  }
}

async function openRun(user: ReturnType<typeof userEvent.setup>, name: RegExp) {
  await openFamily(user, false);
  await user.click(await screen.findByRole('button', { name }));
}

describe('Lab runs', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('starts a quick test on typed symbols and follows the new run', async () => {
    let body: unknown;
    const started = buildRun({ state: 'queued', stage: null });
    setup(
      [],
      [
        http.post('/api/lab/runs', async ({ request }) => {
          body = await request.json();
          return HttpResponse.json(started, { status: 202 });
        }),
        http.get('/api/lab/runs/:id', () => HttpResponse.json(started)),
      ],
    );
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    await openFamily(user);
    await user.type(screen.getByLabelText('Symbols'), 'RELIANCE, TCS');
    await user.click(screen.getByRole('button', { name: 'Run test' }));

    expect(await screen.findByRole('progressbar', { name: 'Run progress' })).toBeInTheDocument();
    expect(body).toEqual({
      strategyId: 'MY_ORB',
      version: 2,
      method: 'quick',
      universe: { symbols: ['RELIANCE', 'TCS'] },
    });
  });

  it('keeps Run test off until a stored version is open and shows the server refusal', async () => {
    setup(
      [],
      [
        http.post('/api/lab/runs', () =>
          problemResponse({ title: 'A charge sheet is needed for a full test.', code: 'lab.invalid_input' }, 422),
        ),
      ],
    );
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    expect(await screen.findByRole('button', { name: 'Run test' })).toBeDisabled();
    await openFamily(user);
    await user.type(screen.getByLabelText('Symbols'), 'TCS');
    await user.click(screen.getByRole('button', { name: 'Run test' }));

    expect((await screen.findAllByText('A charge sheet is needed for a full test.')).length).toBeGreaterThan(0);
  });

  it('shows progress pushed over the hub and reads the run again when it finishes', async () => {
    const hub = createHubFake();
    hub.install();
    let current = buildRun({ progress: { done: 0, total: 10 } });
    setup([current], [http.get('/api/lab/runs/:id', () => HttpResponse.json(current))]);
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    await openRun(user, /MY_ORB v2 · quick/);
    expect(await screen.findByText('quick_test: 0 of 10 instruments')).toBeInTheDocument();
    await waitFor(() => {
      expect(hub.alive).toBe(true);
    });

    hub.labProgress({ runId, state: 'running', stage: 'quick_test', done: 4, total: 10, message: null });
    expect(await screen.findByText('quick_test: 4 of 10 instruments')).toBeInTheDocument();

    current = buildRun({
      state: 'succeeded',
      stage: null,
      counts: { instruments: 10, instrumentsRun: 10, skipped: 0, trades: 42, openAtEnd: 1 },
    });
    hub.labFinished({ runId, state: 'succeeded' });
    expect(await screen.findByText(/42 trades, 10 of 10 instruments replayed/)).toBeInTheDocument();
    expect(screen.queryByRole('progressbar', { name: 'Run progress' })).not.toBeInTheDocument();
  });

  it('polls the run every few seconds when the hub is away and stops when it ends', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let reads = 0;
    let ledgerReads = 0;
    setup(
      [buildRun()],
      [
        http.get('/api/lab/runs/:id', () => {
          reads += 1;
          return HttpResponse.json(reads < 3 ? buildRun() : buildRun({ state: 'succeeded', stage: null }));
        }),
        http.get('/api/lab/strategies/:id/ledger', ({ params }) => {
          ledgerReads += 1;
          return HttpResponse.json({ strategyId: params.id, variantCount: 0, adjustmentFactor: 1, rows: [] });
        }),
      ],
    );
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderWithProviders(<LabPage />);
    await openRun(user, /MY_ORB v2 · quick/);
    expect(await screen.findByRole('progressbar', { name: 'Run progress' })).toBeInTheDocument();
    await waitFor(() => {
      expect(ledgerReads).toBeGreaterThan(0);
    });
    const ledgerBefore = ledgerReads;

    await act(() => vi.advanceTimersByTimeAsync(3100));
    await waitFor(() => {
      expect(reads).toBe(2);
    });
    expect(screen.getByRole('progressbar', { name: 'Run progress' })).toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(3100));
    await waitFor(() => {
      expect(screen.queryByRole('progressbar', { name: 'Run progress' })).not.toBeInTheDocument();
    });
    expect(screen.getByRole('status')).toHaveTextContent('Run succeeded');
    await waitFor(() => {
      expect(ledgerReads).toBeGreaterThan(ledgerBefore);
    });

    const done = reads;
    await act(() => vi.advanceTimersByTimeAsync(30_000));
    expect(reads).toBe(done);
  });

  it('keeps a slow safety poll while the hub is connected', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const hub = createHubFake();
    hub.install();
    let reads = 0;
    setup(
      [buildRun()],
      [
        http.get('/api/lab/runs/:id', () => {
          reads += 1;
          return HttpResponse.json(buildRun());
        }),
      ],
    );
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderWithProviders(<LabPage />);
    await openRun(user, /MY_ORB v2 · quick/);
    expect(await screen.findByRole('progressbar', { name: 'Run progress' })).toBeInTheDocument();
    await waitFor(() => {
      expect(hub.alive).toBe(true);
    });
    await act(() => vi.advanceTimersByTimeAsync(1000));
    const before = reads;

    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(reads).toBe(before);
    await act(() => vi.advanceTimersByTimeAsync(11_000));
    await waitFor(() => {
      expect(reads).toBeGreaterThan(before);
    });
  });

  it('does not let an older progress event revive a finished run', async () => {
    const hub = createHubFake();
    hub.install();
    const done = buildRun({ state: 'succeeded', stage: null });
    setup([done], [http.get('/api/lab/runs/:id', () => HttpResponse.json(done))]);
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    await openRun(user, /MY_ORB v2 · quick/);
    expect(await screen.findByRole('status')).toHaveTextContent('Run succeeded');
    await waitFor(() => {
      expect(hub.alive).toBe(true);
    });
    hub.labProgress({ runId, state: 'running', stage: 'quick_test', done: 1, total: 10, message: null });
    await new Promise((r) => setTimeout(r, 20));
    expect(screen.getByRole('status')).toHaveTextContent('Run succeeded');
  });

  it('says so when the verdict and the report list no limits', async () => {
    const done = buildRun({ state: 'succeeded', stage: null, method: 'full', verdict: { ...verdict, limits: [] } });
    setup(
      [done],
      [
        http.get('/api/lab/runs/:id', () => HttpResponse.json(done)),
        http.get('/api/lab/runs/:id/report', () => HttpResponse.json({ ...report, honestLimits: [] })),
      ],
    );
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    await openRun(user, /MY_ORB v2 · full/);

    expect(await screen.findByRole('region', { name: 'Report' })).toBeInTheDocument();
    expect(await screen.findAllByText(/No limits were listed/)).toHaveLength(2);
  });

  it('cancels a running run', async () => {
    let cancelled = false;
    setup(
      [buildRun()],
      [
        http.get('/api/lab/runs/:id', () => HttpResponse.json(buildRun({ cancelRequested: cancelled }))),
        http.post('/api/lab/runs/:id/cancel', () => {
          cancelled = true;
          return HttpResponse.json(buildRun({ cancelRequested: true }), { status: 202 });
        }),
      ],
    );
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    await openRun(user, /MY_ORB v2 · quick/);
    await user.click(await screen.findByRole('button', { name: 'Cancel run' }));

    expect(await screen.findByText('running (cancel requested)')).toBeInTheDocument();
  });

  it('shows the verdict gates with adjusted thresholds and honest limits, and the report with its links', async () => {
    const done = buildRun({ state: 'succeeded', stage: null, method: 'full', verdict });
    setup(
      [done],
      [
        http.get('/api/lab/runs/:id', () => HttpResponse.json(done)),
        http.get('/api/lab/runs/:id/report', () => HttpResponse.json(report)),
        http.get('/api/capabilities', () =>
          HttpResponse.json({
            items: [{ name: 'exports', label: 'Exports', meaning: 'm', enabled: true, changedAt: null, reason: null }],
            history: [],
          }),
        ),
      ],
    );
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    await openRun(user, /MY_ORB v2 · full/);

    const gates = await screen.findByRole('table', { name: 'Gates' });
    expect(within(gates).getByText('130 trades against 120')).toBeInTheDocument();
    expect(within(gates).getByText('Met')).toBeInTheDocument();
    expect(within(gates).getByText('Missed')).toBeInTheDocument();
    expect(screen.getByText(/4 variants of this family tested; thresholds x1.2/)).toBeInTheDocument();
    expect(screen.getByText('About 2.1 years of data, no hold-out.')).toBeInTheDocument();

    expect(await screen.findByText('Marginal: expectancy is below the adjusted gate.')).toBeInTheDocument();
    expect(screen.getByText('A small rise in costs erases the edge.')).toBeInTheDocument();
    expect(screen.getByText('Full-period figures are in-sample exploration.')).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: 'Open report page' })).toHaveAttribute(
      'href',
      `/api/lab/runs/${runId}/report/html`,
    );
    expect(screen.getByRole('link', { name: 'Download folder (zip)' })).toHaveAttribute(
      'href',
      `/api/lab/runs/${runId}/report/folder`,
    );
  });

  it('draws no download link while the exports switch is off, only the words and a link to the switches (ADR 0131)', async () => {
    const done = buildRun({ state: 'succeeded', stage: null, method: 'full', verdict });
    setup(
      [done],
      [
        http.get('/api/lab/runs/:id', () => HttpResponse.json(done)),
        http.get('/api/lab/runs/:id/report', () => HttpResponse.json(report)),
      ],
    );
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    await openRun(user, /MY_ORB v2 · full/);

    expect(await screen.findByText(/Report downloads are switched off/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'owner-act switches' })).toHaveAttribute('href', '/owner-acts');
    expect(screen.queryByRole('link', { name: 'Open report page' })).toBeNull();
    expect(screen.queryByRole('link', { name: 'Download folder (zip)' })).toBeNull();
  });

  it('shows an error when the report cannot be read', async () => {
    const done = buildRun({ state: 'succeeded', stage: null, method: 'full', verdict });
    setup(
      [done],
      [
        http.get('/api/lab/runs/:id', () => HttpResponse.json(done)),
        http.get('/api/lab/runs/:id/report', () => problemResponse({ title: 'Report unavailable.' }, 409)),
      ],
    );
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    await openRun(user, /MY_ORB v2 · full/);

    expect(await screen.findByText('Report unavailable.')).toBeInTheDocument();
  });

  it('shows an error when the runs cannot be read', async () => {
    setup([], [http.get('/api/lab/runs', () => problemResponse({ title: 'Runs unavailable.' }, 503))]);
    renderWithProviders(<LabPage />);
    expect(await screen.findByText('Runs unavailable.')).toBeInTheDocument();
  });

  it('lists the ledger with a link to each real run and none for a sweep point', async () => {
    const done = buildRun({ state: 'succeeded', stage: null });
    setup(
      [done],
      [
        http.get('/api/lab/runs/:id', () => HttpResponse.json(done)),
        http.get('/api/lab/strategies/:id/ledger', ({ params }) =>
          HttpResponse.json({
            strategyId: params.id,
            variantCount: 3,
            adjustmentFactor: 1.1,
            rows: [ledgerRow(runId, 'quick'), ledgerRow(sweepId, 'sweep')],
          }),
        ),
      ],
    );
    const user = userEvent.setup();
    renderWithProviders(<LabPage />);
    await openFamily(user, false);

    const table = await screen.findByRole('table', { name: 'Test ledger' });
    expect(screen.getByText(/3 variants counted; thresholds are tightened x1.1/)).toBeInTheDocument();
    expect(within(table).getAllByRole('button', { name: 'Open run' })).toHaveLength(1);
    expect(within(table).getByText('parameter sweep point')).toBeInTheDocument();
    await user.click(within(table).getByRole('button', { name: 'Open run' }));
    expect(await screen.findByText('MY_ORB v2 · quick', { selector: 'dd' })).toBeInTheDocument();
  });
});
