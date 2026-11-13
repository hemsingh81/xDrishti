import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Schemas } from '@/shared/api';
import { pinClock } from '@/test/pinClock';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { HoldOutReplayCard } from './components/replay/HoldOutReplayCard';

pinClock('2027-04-20T04:00:00Z');

type Registration = Schemas['HoldOutRegistrationView'];

const ID = '44444444-4444-4444-4444-444444444444';

const registration = (over: Partial<Registration> = {}): Registration => ({
  id: ID,
  blockStart: '2027-05-05',
  blockEnd: '2027-11-04',
  configHash: 'a'.repeat(64),
  mode: 'owner',
  actor: 'owner:x',
  createdAt: '2027-04-20T03:00:00Z',
  state: 'waiting',
  driftReason: null,
  rule: { minTrades: 30, maxDrawdownPct: 15 },
  costSheetId: 'dhan-2026-04',
  costSheetRatesHash: 'b'.repeat(64),
  capital: 1_000_000,
  instruments: 40,
  engineVersion: 'sim-5',
  settingsHash: 'c'.repeat(64),
  hindsight: 'Unseen by the system; the owner may have seen these prices elsewhere.',
  result: null,
  ...over,
});

const arm = (name: string): Schemas['SystemArmFigures'] => ({
  arm: name,
  closedTrades: 41,
  capital: 1_000_000,
  finalEquity: 1_031_000,
  netReturnPct: 3.1,
  cagrPct: 6.3,
  maxDrawdownPct: 7.4,
  netBasis: true,
  basis: 'after the stored sheet',
  sessions: 125,
});

const serve = (rows: Registration[]) => {
  server.use(http.get('/api/replay/holdout-registrations', () => HttpResponse.json(rows)));
};

describe('HoldOutReplayCard (SR-03)', () => {
  it('shows the rule first, the frozen hash, the target block and the hindsight label, with both switches on', async () => {
    serve([registration()]);
    renderWithProviders(<HoldOutReplayCard />);

    const card = await screen.findByRole('region', { name: 'Hold-out replay' });
    expect(within(card).getByText(/a pass needs at least 30 closed trades/)).toBeInTheDocument();
    expect(within(card).getByText(/maximum drawdown of at most 15 %/)).toBeInTheDocument();
    expect(within(card).getByText(`Frozen hash ${'a'.repeat(64)}`)).toBeInTheDocument();
    expect(
      within(card).getByText(/Unseen by the system; the owner may have seen these prices elsewhere/),
    ).toBeInTheDocument();
    expect(within(card).getByText('Waiting for the block to seal')).toBeInTheDocument();
    expect(within(card).getByRole('switch', { name: /Register automatically/ })).toBeChecked();
    expect(within(card).getByRole('switch', { name: /Run automatically/ })).toBeChecked();
  });

  it('shows the verdict with both arms once it ran, and no run button', async () => {
    serve([
      registration({
        state: 'run',
        result: {
          verdict: 'pass',
          reason: '41 trades, CAGR 6.3 %',
          figures: {
            selection: arm('selection'),
            baseline: arm('baseline'),
            comparison: { answerCode: 'no_measurable_difference' } as unknown as Schemas['ReplayArmResult'],
            rule: { minTrades: 30, maxDrawdownPct: 15 },
            labels: [],
            engineChanged: false,
            failure: null,
          },
          runIds: [],
          engineVersions: 'registered=sim-5;current=sim-5',
          mode: 'auto',
          actor: 'system:holdout-replay',
          at: '2027-11-05T00:00:00Z',
        },
      }),
    ]);
    renderWithProviders(<HoldOutReplayCard />);

    expect(await screen.findByText('Verdict: pass (auto)')).toBeInTheDocument();
    expect(screen.getByText(/Selection: 41 trades, CAGR 6.3 %, max drawdown 7.4 %, net/)).toBeInTheDocument();
    expect(screen.getByText('Comparison: no measurable difference.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Run once/ })).not.toBeInTheDocument();
  });

  it('says a failed run reached no verdict, with its reason, and never as a verdict', async () => {
    serve([
      registration({
        state: 'run',
        result: {
          verdict: 'run_failed',
          reason: 'run_failed: InvalidOperationException',
          figures: {
            selection: null,
            baseline: null,
            comparison: null,
            rule: { minTrades: 30, maxDrawdownPct: 15 },
            labels: [],
            engineChanged: false,
            failure: 'run_failed: InvalidOperationException',
            snapshotKeys: null,
            snapshotCreatedAt: null,
          },
          runIds: [],
          engineVersions: 'registered=sim-5;current=sim-5',
          mode: 'owner',
          actor: 'owner:x',
          at: '2027-11-05T00:00:00Z',
        },
      }),
    ]);
    renderWithProviders(<HoldOutReplayCard />);

    expect(await screen.findByText('The run failed and reached no verdict (owner)')).toBeInTheDocument();
    expect(screen.getByText('run_failed: InvalidOperationException')).toBeInTheDocument();
    expect(screen.queryByText(/^Verdict:/)).not.toBeInTheDocument();
  });

  it('runs once behind the password dialog, which says the read is irreversible, and says a drift refusal in words', async () => {
    serve([registration()]);
    server.use(
      http.post(`/api/replay/holdout-registrations/${ID}/run`, () =>
        problemResponse({ status: 409, title: 'drift', code: 'replay.registration_drift' }, 409),
      ),
    );
    renderWithProviders(<HoldOutReplayCard />);

    await userEvent.click(await screen.findByRole('button', { name: 'Run once (irreversible)' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/cannot be undone/)).toBeInTheDocument();
    await userEvent.type(within(dialog).getByLabelText(/Your password/), 'secret');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Run once' }));

    expect(await within(dialog).findByText(/no price of the block was read/)).toBeInTheDocument();
  });

  it('changes a switch only with the password', async () => {
    let sent: unknown;
    server.use(
      http.put('/api/replay/settings', async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json({ autoRegister: true, autoRun: false });
      }),
    );
    renderWithProviders(<HoldOutReplayCard />);

    await userEvent.click(await screen.findByRole('switch', { name: /Run automatically/ }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText(/Your password/), 'secret');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Turn off' }));

    await waitFor(() => {
      expect(sent).toEqual({ password: 'secret', autoRun: false });
    });
  });
});
