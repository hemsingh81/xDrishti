import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { sessionQueryKey, type SessionState } from '@/features/auth';
import type { Schemas } from '@/shared/api';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { RegistrationCard } from './components/RegistrationCard';
import { ReportPanel } from './components/ReportPanel';

type Registration = Schemas['RegistrationView'];
type Report = Schemas['LabReportView'];
type HoldOut = Schemas['ReportHoldOutView'];
type Result = Schemas['HoldOutResultView'];
type Run = Schemas['LabRunView'];

const runId = '00000000-0000-4000-8000-0000000000a1';
const jobId = '00000000-0000-4000-8000-0000000000b2';
const regUrl = '/api/lab/strategies/MY_ORB/versions/2/registration';
const markup = '<img src=x onerror="alert(1)"> forced seller';

const stored = (over: Partial<Registration> = {}): Registration => ({
  strategyId: 'MY_ORB',
  version: 2,
  counterparty: 'Forced sellers at the open',
  parameterSigns: 'Wider range, weaker edge',
  applicability: 'Tier A, first hour',
  expectedEdge: '40 bps gross against 25 bps cost',
  falsifier: 'OOS expectancy under 0.1 R',
  textHash: 'f'.repeat(64),
  createdAt: '2026-11-04T04:00:00Z',
  actor: 'owner',
  ...over,
});

const result = (over: Partial<Result> = {}): Result => ({
  strategyId: 'MY_ORB',
  version: 2,
  kind: 'strategy',
  mode: 'owner',
  state: 'used',
  unlockedAt: '2026-11-04T05:00:00Z',
  from: '2026-05-01',
  to: '2026-10-30',
  reusedWindow: false,
  runSawHoldOut: false,
  passed: true,
  status: 'pass',
  reason: 'Hold-out kept 60 % of development.',
  developmentExpectancyR: 0.2,
  holdOutExpectancyR: 0.12,
  ratio: 0.6,
  trades: 41,
  planHash: 'a'.repeat(64),
  settingHash: 'b'.repeat(64),
  actor: 'owner',
  notes: [],
  ...over,
});

const job = (over: Partial<Run> = {}): Run => ({
  id: jobId,
  key: 'k'.repeat(64),
  state: 'queued',
  stage: null,
  strategy: { id: 'MY_ORB', version: 2, hash: 'a'.repeat(64) },
  universe: {
    kind: 'symbols',
    basketId: null,
    label: '2 symbols',
    requested: 2,
    instruments: [],
    sampled: false,
    seed: null,
  },
  window: { from: '2025-10-01', to: '2026-04-30' },
  method: 'holdout',
  progress: { done: 0, total: 0 },
  simRunKeys: [],
  counts: null,
  problems: [],
  stages: [],
  labels: [],
  error: null,
  cancelRequested: false,
  createdAt: '2026-11-05T04:00:00Z',
  startedAt: null,
  finishedAt: null,
  verdict: null,
  ...over,
});

const holdOut = (over: Partial<HoldOut> = {}): HoldOut => ({
  state: 'kept',
  enforce: false,
  start: '2026-05-01',
  lengthMonths: 6,
  runReachesHoldOut: false,
  text: 'Hold-out: kept, not yet used.',
  result: null,
  ...over,
});

const report = (over: Partial<Report> = {}): Report => ({
  runId,
  outcome: 'MARGINAL',
  variantCount: 28,
  adjustmentFactor: 2.2018,
  run: {} as Schemas['ReportRunView'],
  kpis: { oosTrades: 130, expectancyR: 0.2, profitFactor: 1.1, maxDrawdownR: 4, folds: 5, positiveFolds: 3 },
  gates: [],
  bestCells: [],
  exploration: [],
  robustness: null,
  warnings: [],
  honestLimits: [],
  notIncluded: [],
  labels: [],
  summary: 'Marginal.',
  registration: {
    state: 'registered',
    enforce: false,
    registration: stored(),
    text: 'Registered before the data was searched.',
  },
  trials: {
    runHashes: 3,
    sweepPoints: 16,
    extraExits: 9,
    replans: 0,
    nullRows: 0,
    total: 28,
    honestFactor: 2.2018,
    judgedCount: 19,
    factorUsed: 2.062,
    honestCountApplied: false,
    text: 'N = 3 + 9 + 0 + 0 + 16.',
  },
  holdOut: holdOut(),
  ...over,
});

describe('Registration of a version', () => {
  it('shows the loading state and then the form with a counter for each of the five fields', async () => {
    renderWithProviders(<RegistrationCard strategyId="MY_ORB" version={2} />);
    expect(screen.getByRole('status')).toHaveTextContent('Loading the registration');
    const form = await screen.findByRole('form', { name: 'Register this version' });
    expect(within(form).getAllByRole('textbox')).toHaveLength(5);
    expect(within(form).getAllByText(/0 of 2000 characters/)).toHaveLength(5);
    expect(within(form).getByRole('button', { name: 'Register' })).toBeDisabled();
  });

  it('sends the five texts once all are filled, then shows the stored registration read-only with the text escaped', async () => {
    let body: unknown;
    let saved: Registration | null = null;
    server.use(
      http.get(regUrl, () =>
        saved ? HttpResponse.json(saved) : problemResponse({ title: 'None.', code: 'lab.not_found' }, 404),
      ),
      http.put(regUrl, async ({ request }) => {
        body = await request.json();
        saved = stored({ counterparty: markup });
        return HttpResponse.json(saved);
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<RegistrationCard strategyId="MY_ORB" version={2} />);
    const form = await screen.findByRole('form', { name: 'Register this version' });
    const boxes = within(form).getAllByRole('textbox');
    const texts = [markup, 'wider is weaker', 'tier A', '40 bps against 25 bps', 'under 0.1 R'];
    for (const [i, text] of texts.entries()) {
      await user.click(boxes[i]!);
      await user.paste(text);
    }
    expect(within(form).getByText(new RegExp(`${markup.length} of 2000 characters`))).toBeInTheDocument();
    await user.click(within(form).getByRole('button', { name: 'Register' }));

    expect(await screen.findByText(/Frozen: it cannot be changed/)).toBeInTheDocument();
    expect(body).toEqual({
      counterparty: markup,
      parameterSigns: 'wider is weaker',
      applicability: 'tier A',
      expectedEdge: '40 bps against 25 bps',
      falsifier: 'under 0.1 R',
    });
    expect(screen.queryByRole('form', { name: 'Register this version' })).toBeNull();
    // The text is shown as text: the markup is on the page as characters, never as an element.
    expect(screen.getByText(markup)).toBeInTheDocument();
    expect(document.querySelector('img')).toBeNull();
  });

  it('shows a registered version read-only, with no way to change it', async () => {
    server.use(http.get(regUrl, () => HttpResponse.json(stored({ falsifier: '<b>retire</b>' }))));
    renderWithProviders(<RegistrationCard strategyId="MY_ORB" version={2} />);
    expect(await screen.findByText('<b>retire</b>')).toBeInTheDocument();
    expect(screen.queryAllByRole('textbox')).toHaveLength(0);
    expect(screen.queryByRole('button', { name: 'Register' })).toBeNull();
    expect(screen.getByText(/Registered by owner/)).toBeInTheDocument();
  });

  it('refuses a part over 2000 characters before sending anything', async () => {
    const user = userEvent.setup();
    renderWithProviders(<RegistrationCard strategyId="MY_ORB" version={2} />);
    const form = await screen.findByRole('form', { name: 'Register this version' });
    for (const box of within(form).getAllByRole('textbox')) {
      await user.click(box);
      await user.paste('x'.repeat(2001));
    }
    expect(within(form).getAllByText(/2001 of 2000 characters/)).toHaveLength(5);
    expect(within(form).getByRole('button', { name: 'Register' })).toBeDisabled();
  });

  it.each([
    ['lab.registration_frozen', 'This version can no longer be registered'],
    ['lab.registration_exists', 'This version is already registered'],
  ])('says in plain words that a 409 %s stored nothing', async (code, words) => {
    server.use(http.put(regUrl, () => problemResponse({ title: 'Conflict.', code }, 409)));
    const user = userEvent.setup();
    renderWithProviders(<RegistrationCard strategyId="MY_ORB" version={2} />);
    const form = await screen.findByRole('form', { name: 'Register this version' });
    for (const box of within(form).getAllByRole('textbox')) {
      await user.click(box);
      await user.paste('text');
    }
    await user.click(within(form).getByRole('button', { name: 'Register' }));
    expect(await screen.findByText(new RegExp(`${words}.*Nothing was stored`))).toBeInTheDocument();
  });

  it('disables the form after a 409 registration_frozen and keeps the message', async () => {
    server.use(http.put(regUrl, () => problemResponse({ title: 'Conflict.', code: 'lab.registration_frozen' }, 409)));
    const user = userEvent.setup();
    renderWithProviders(<RegistrationCard strategyId="MY_ORB" version={2} />);
    const form = await screen.findByRole('form', { name: 'Register this version' });
    for (const box of within(form).getAllByRole('textbox')) {
      await user.click(box);
      await user.paste('text');
    }
    await user.click(within(form).getByRole('button', { name: 'Register' }));
    expect(await screen.findByText(/This version can no longer be registered/)).toBeInTheDocument();
    for (const box of within(form).getAllByRole('textbox')) expect(box).toBeDisabled();
    expect(within(form).getByRole('button', { name: 'Register' })).toBeDisabled();
  });

  it('refreshes the report of runs after a registration', async () => {
    const user = userEvent.setup();
    const view = renderWithProviders(<RegistrationCard strategyId="MY_ORB" version={2} />);
    view.client.setQueryData(['lab', 'runs', 'report', runId], report());
    server.use(http.put(regUrl, () => HttpResponse.json(stored())));
    const form = await screen.findByRole('form', { name: 'Register this version' });
    for (const box of within(form).getAllByRole('textbox')) {
      await user.click(box);
      await user.paste('text');
    }
    await user.click(within(form).getByRole('button', { name: 'Register' }));
    await waitFor(() => {
      expect(view.client.getQueryState(['lab', 'runs', 'report', runId])?.isInvalidated).toBe(true);
    });
  });

  it('shows the server message when the registration cannot be read', async () => {
    server.use(http.get(regUrl, () => problemResponse({ title: 'The registry is down.' }, 500)));
    renderWithProviders(<RegistrationCard strategyId="MY_ORB" version={2} />);
    expect(await screen.findByText('The registry is down.')).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Register this version' })).toBeNull();
  });
});

describe('Trials and hold-out on the report', () => {
  const serve = (r: Report) => {
    server.use(http.get('/api/lab/runs/:id/report', () => HttpResponse.json(r)));
  };

  it('shows the registration state, the trial count with its parts and the factor', async () => {
    serve(report());
    renderWithProviders(<ReportPanel runId={runId} />);
    const parts = await screen.findByRole('table', { name: 'Trial count parts' });
    expect(within(parts).getByRole('row', { name: 'Distinct strategy texts 3' })).toBeInTheDocument();
    expect(within(parts).getByRole('row', { name: 'Extra exit candidates 9' })).toBeInTheDocument();
    expect(within(parts).getByRole('row', { name: 'Replans 0' })).toBeInTheDocument();
    expect(within(parts).getByRole('row', { name: 'Sweep points 16' })).toBeInTheDocument();
    expect(screen.getByText(/28 looks at this strategy/)).toBeInTheDocument();
    expect(screen.getByText(/Honest factor x2.2.*tightened by x2.06.*not applied yet/)).toBeInTheDocument();
    expect(screen.getByText('Registered')).toBeInTheDocument();
  });

  it('says not registered in words', async () => {
    serve(
      report({
        registration: {
          state: 'not_registered',
          enforce: false,
          registration: null,
          text: 'No pre-registration was written.',
        },
      }),
    );
    renderWithProviders(<ReportPanel runId={runId} />);
    expect(await screen.findByText('Not registered')).toBeInTheDocument();
  });

  it('keeps the hold-out numbers hidden before an unlock, even if a result came with a kept block', async () => {
    serve(report({ holdOut: holdOut({ result: result() }) }));
    renderWithProviders(<ReportPanel runId={runId} />);
    const card = await screen.findByRole('region', { name: 'Hold-out' });
    expect(within(card).getByText('Kept, not yet used')).toBeInTheDocument();
    expect(within(card).getByText(/The block starts .*2026 and is 6 months long/)).toBeInTheDocument();
    expect(within(card).getByText(/stay hidden until the owner unlocks/)).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Unlock the hold-out' })).toBeInTheDocument();
    expect(within(card).queryByText(/expectancy/i)).toBeNull();
    expect(within(card).queryByText(/Hold-out trades/)).toBeNull();
    expect(within(card).queryByText(/41/)).toBeNull();
  });

  it('says a block is not fixed yet and offers no unlock', async () => {
    serve(
      report({
        holdOut: holdOut({
          state: 'not_fixed',
          start: null,
          lengthMonths: null,
          text: 'Hold-out: no block is fixed yet.',
        }),
      }),
    );
    renderWithProviders(<ReportPanel runId={runId} />);
    const card = await screen.findByRole('region', { name: 'Hold-out' });
    expect(within(card).getByText('Not fixed yet')).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'Unlock the hold-out' })).toBeNull();
  });

  it('shows the numbers of a stored look and no unlock button', async () => {
    serve(report({ holdOut: holdOut({ state: 'used', text: 'Hold-out: used once.', result: result() }) }));
    renderWithProviders(<ReportPanel runId={runId} />);
    const card = await screen.findByRole('region', { name: 'Hold-out' });
    expect(within(card).getByText('Used once')).toBeInTheDocument();
    expect(within(card).getByText(/Hold-out rule: pass. Counts toward a pass/)).toBeInTheDocument();
    expect(within(card).getByText('0.12 R')).toBeInTheDocument();
    expect(within(card).getByText('41')).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'Unlock the hold-out' })).toBeNull();
  });

  it('says a look at a block seen before cannot count toward a pass', async () => {
    serve(
      report({
        holdOut: holdOut({
          state: 'reused',
          text: 'Hold-out: looked at again.',
          result: result({ state: 'reused', reusedWindow: true, passed: false }),
        }),
      }),
    );
    renderWithProviders(<ReportPanel runId={runId} />);
    const card = await screen.findByRole('region', { name: 'Hold-out' });
    expect(within(card).getByText('Looked at again')).toBeInTheDocument();
    expect(within(card).getByText(/Does not count toward a pass.*cannot count toward a pass/)).toBeInTheDocument();
  });

  it('asks for the password, queues the look, clears the field and shows the stored numbers once the job has succeeded', async () => {
    let unlocked = false;
    let body: unknown;
    server.use(
      http.get('/api/lab/runs/:id/report', () =>
        HttpResponse.json(
          unlocked
            ? report({ holdOut: holdOut({ state: 'used', text: 'Hold-out: used once.', result: result() }) })
            : report(),
        ),
      ),
      http.post('/api/lab/runs/:id/holdout-unlock', async ({ request }) => {
        body = await request.json();
        unlocked = true;
        return HttpResponse.json(job(), { status: 202 });
      }),
      http.get('/api/lab/runs/:id', () => HttpResponse.json(job({ state: 'succeeded' }))),
    );
    const user = userEvent.setup();
    const view = renderWithProviders(<ReportPanel runId={runId} />);
    await user.click(await screen.findByRole('button', { name: 'Unlock the hold-out' }));
    const dialog = await screen.findByRole('dialog');
    await waitFor(() => {
      expect(within(dialog).getByLabelText(/Your password for this act/)).toHaveFocus();
    });
    expect(within(dialog).getByRole('button', { name: 'Unlock' })).toBeDisabled();
    await user.type(within(dialog).getByLabelText(/Your password for this act/), 'secret');
    await user.click(within(dialog).getByRole('button', { name: 'Unlock' }));

    expect(
      await screen.findByText('Hold-out rule: pass. Counts toward a pass. Hold-out kept 60 % of development.'),
    ).toBeInTheDocument();
    expect(body).toEqual({ password: 'secret' });
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).toBeNull();
    });
    expect(screen.queryByRole('button', { name: 'Unlock the hold-out' })).toBeNull();
    // The password does not stay in the mutation cache either.
    expect(
      JSON.stringify(
        view.client
          .getMutationCache()
          .getAll()
          .map((m) => m.state.variables ?? null),
      ),
    ).not.toContain('secret');
  });

  it('warns that a run whose window reached the block can never pass, and that the look is spent', async () => {
    serve(report({ holdOut: holdOut({ runReachesHoldOut: true }) }));
    const user = userEvent.setup();
    renderWithProviders(<ReportPanel runId={runId} />);
    await user.click(await screen.findByRole('button', { name: 'Unlock the hold-out' }));
    const dialog = await screen.findByRole('dialog');
    expect(
      within(dialog).getByText(
        "This run's window reached the block: this look can never count toward a pass, and it spends the block's one fresh look.",
      ),
    ).toBeInTheDocument();
  });

  it('gives no such warning when the run stayed clear of the block', async () => {
    serve(report());
    const user = userEvent.setup();
    renderWithProviders(<ReportPanel runId={runId} />);
    await user.click(await screen.findByRole('button', { name: 'Unlock the hold-out' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).queryByText(/window reached the block/)).toBeNull();
  });

  it('refreshes the report when the queued job has succeeded', async () => {
    let reads = 0;
    server.use(
      http.get('/api/lab/runs/:id/report', () => {
        reads += 1;
        return HttpResponse.json(report());
      }),
      http.post('/api/lab/runs/:id/holdout-unlock', () => HttpResponse.json(job(), { status: 202 })),
      http.get('/api/lab/runs/:id', () => HttpResponse.json(job({ state: 'succeeded' }))),
    );
    const user = userEvent.setup();
    renderWithProviders(<ReportPanel runId={runId} />);
    await user.click(await screen.findByRole('button', { name: 'Unlock the hold-out' }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText(/Your password for this act/), 'secret');
    await user.click(within(dialog).getByRole('button', { name: 'Unlock' }));
    await waitFor(() => {
      expect(reads).toBeGreaterThanOrEqual(2);
    });
  });

  it('says the look is queued while the job is going and offers no second unlock', async () => {
    serve(report());
    server.use(
      http.post('/api/lab/runs/:id/holdout-unlock', () => HttpResponse.json(job(), { status: 202 })),
      http.get('/api/lab/runs/:id', () => HttpResponse.json(job({ state: 'running' }))),
    );
    const user = userEvent.setup();
    renderWithProviders(<ReportPanel runId={runId} />);
    await user.click(await screen.findByRole('button', { name: 'Unlock the hold-out' }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText(/Your password for this act/), 'secret');
    await user.click(within(dialog).getByRole('button', { name: 'Unlock' }));

    const card = await screen.findByRole('region', { name: 'Hold-out' });
    expect(await within(card).findByText('The hold-out look is running.')).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'Unlock the hold-out' })).toBeNull();
    expect(within(card).getByText(/stay hidden until the owner unlocks/)).toBeInTheDocument();
  });

  it('says why a job that failed stored nothing and lets the owner ask again', async () => {
    serve(report());
    server.use(
      http.post('/api/lab/runs/:id/holdout-unlock', () => HttpResponse.json(job(), { status: 202 })),
      http.get('/api/lab/runs/:id', () =>
        HttpResponse.json(
          job({
            state: 'failed',
            error: 'lab.holdout_unavailable',
            problems: [
              {
                stage: 'holdout',
                path: 'holdout',
                code: 'lab.holdout_unavailable',
                message: 'The plan of this run differs from the plan the version froze first.',
                severity: 'error',
                line: null,
                column: null,
              },
            ],
          }),
        ),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<ReportPanel runId={runId} />);
    await user.click(await screen.findByRole('button', { name: 'Unlock the hold-out' }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText(/Your password for this act/), 'secret');
    await user.click(within(dialog).getByRole('button', { name: 'Unlock' }));

    const card = await screen.findByRole('region', { name: 'Hold-out' });
    expect(
      await within(card).findByText(/did not complete \(failed\). Nothing was stored. The plan of this run differs/),
    ).toBeInTheDocument();
    await user.click(within(card).getByRole('button', { name: 'Try again' }));
    expect(within(card).getByRole('button', { name: 'Unlock the hold-out' })).toBeInTheDocument();
  });

  it.each([
    ['interrupted', null],
    ['failed', 'lab.holdout_used'],
  ])(
    'does not claim nothing was stored when a %s job (%s) may have stored the look, and reads the report again',
    async (state, error) => {
      let reads = 0;
      server.use(
        http.get('/api/lab/runs/:id/report', () => {
          reads += 1;
          return HttpResponse.json(report());
        }),
        http.post('/api/lab/runs/:id/holdout-unlock', () => HttpResponse.json(job(), { status: 202 })),
        http.get('/api/lab/runs/:id', () => HttpResponse.json(job({ state, error }))),
      );
      const user = userEvent.setup();
      renderWithProviders(<ReportPanel runId={runId} />);
      await user.click(await screen.findByRole('button', { name: 'Unlock the hold-out' }));
      const dialog = await screen.findByRole('dialog');
      await user.type(within(dialog).getByLabelText(/Your password for this act/), 'secret');
      await user.click(within(dialog).getByRole('button', { name: 'Unlock' }));

      const card = await screen.findByRole('region', { name: 'Hold-out' });
      expect(await within(card).findByText(/Open the report to see whether the look was stored\./)).toBeInTheDocument();
      expect(within(card).queryByText(/Nothing was stored/)).toBeNull();
      await waitFor(() => {
        expect(reads).toBeGreaterThanOrEqual(2);
      });
    },
  );

  it('says the progress could not be read when the job cannot be fetched', async () => {
    serve(report());
    server.use(
      http.post('/api/lab/runs/:id/holdout-unlock', () => HttpResponse.json(job(), { status: 202 })),
      http.get('/api/lab/runs/:id', () => problemResponse({ title: 'Down.' }, 500)),
    );
    const user = userEvent.setup();
    renderWithProviders(<ReportPanel runId={runId} />);
    await user.click(await screen.findByRole('button', { name: 'Unlock the hold-out' }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText(/Your password for this act/), 'secret');
    await user.click(within(dialog).getByRole('button', { name: 'Unlock' }));

    expect(await screen.findByText(/progress of the hold-out look could not be read/)).toBeInTheDocument();
  });

  it.each([
    [401, 'auth.invalid_credentials', /password was not accepted.*hold-out was not opened/i],
    [423, 'auth.locked', /Too many wrong passwords.*hold-out was not opened/i],
    [409, 'lab.holdout_used', /already looked at the hold-out.*stored result is unchanged/i],
    [409, 'lab.holdout_unavailable', /cannot be opened for this run.*No frozen plan/i],
    [409, 'lab.no_report', /no report to open the hold-out from/i],
    [422, 'lab.holdout_unavailable', /cannot be opened for this run.*No frozen plan/i],
  ])('explains a %s %s in plain words, empties the password and stays signed in', async (status, code, words) => {
    server.use(
      http.get('/api/lab/runs/:id/report', () => HttpResponse.json(report())),
      http.post('/api/lab/runs/:id/holdout-unlock', () => problemResponse({ title: 'No frozen plan.', code }, status)),
    );
    const user = userEvent.setup();
    const view = renderWithProviders(<ReportPanel runId={runId} />);
    await user.click(await screen.findByRole('button', { name: 'Unlock the hold-out' }));
    const dialog = await screen.findByRole('dialog');
    const field = within(dialog).getByLabelText(/Your password for this act/);
    await user.type(field, 'wrong');
    await user.click(within(dialog).getByRole('button', { name: 'Unlock' }));

    expect(await within(dialog).findByText(words)).toBeInTheDocument();
    expect(field).toHaveValue('');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(view.client.getQueryData<SessionState>(sessionQueryKey)?.status).not.toBe('anonymous');
  });

  it('shows the error state when the report cannot be read', async () => {
    server.use(http.get('/api/lab/runs/:id/report', () => problemResponse({ title: 'Report unavailable.' }, 409)));
    renderWithProviders(<ReportPanel runId={runId} />);
    expect(await screen.findByText('Report unavailable.')).toBeInTheDocument();
  });
});
