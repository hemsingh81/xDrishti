import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Schemas } from '@/shared/api';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { LivePage } from './components/LivePage';

pinClock('2026-10-28T06:30:00Z');

const ramp = (over: Partial<Schemas['RampView']> = {}): Schemas['RampView'] => ({
  accountId: 'a',
  step: null,
  scaleEffective: null,
  hold: false,
  holdReason: null,
  nextStep: null,
  eligible: false,
  gatesBuilt: true,
  intendedRiskPct: 0.5,
  liveRiskPct: null,
  writesEnabled: true,
  history: [],
  notice: 'x',
  gates: [],
  breaches: [],
  holdConditions: [],
  loss: null,
  halts: [],
  slippageKind: null,
  autoRampUp: false,
  exitConfirmed: false,
  sessions: 0,
  ...over,
});

const gate = (code: string, state: string, over: Partial<Schemas['RampGateView']> = {}): Schemas['RampGateView'] => ({
  code,
  state,
  number: 1,
  threshold: 1,
  n: 20,
  reason: 'because',
  ...over,
});

const switches = (over: Partial<Schemas['LiveSettingsView']> = {}): Schemas['LiveSettingsView'] => ({
  id: null,
  autoApproveTickets: false,
  autoRampUp: false,
  writesEnabled: true,
  notice: 'x',
  history: [],
  ...over,
});

function mount(
  r: Schemas['RampView'],
  s: Schemas['LiveSettingsView'],
  onStep?: (body: unknown) => Response | undefined,
) {
  const posts: unknown[] = [];
  server.use(
    http.get('/api/live-trading/ramp', () => HttpResponse.json(r)),
    http.get('/api/live-trading/settings', () => HttpResponse.json(s)),
    http.post('/api/live-trading/ramp/steps', async ({ request }) => {
      const body = await request.json();
      posts.push(body);
      return onStep?.(body) ?? HttpResponse.json(ramp({ step: 1, scaleEffective: 0.25, liveRiskPct: 0.125 }));
    }),
  );
  renderRoutes([{ path: '/live', element: <LivePage /> }], { initialPath: '/live?tab=ramp' });
  return posts;
}

describe('the Ramp tab', () => {
  it('says no step is set and that both switches are OFF, with the no-order sentence', async () => {
    mount(ramp(), switches());

    expect(await screen.findByText('No step is set, so no live ticket is sized for this account.')).toBeInTheDocument();
    expect(screen.getByText('Approve live tickets by rule: OFF')).toBeInTheDocument();
    expect(screen.getByText('Raise S1 to S2 by rule: OFF')).toBeInTheDocument();
    expect(screen.getAllByText(/No setting lets this system place, change or cancel an order/).length).toBeGreaterThan(
      0,
    );
    expect(screen.getByRole('button', { name: 'Set the first step' })).toBeEnabled();
    expect(screen.getByText('Not eligible to move up.')).toBeInTheDocument();
    expect(screen.getByText('No evidence yet: the gates appear once a step is set.')).toBeInTheDocument();
  });

  it('shows the live risk at the step and never a digit for a value that is not known', async () => {
    mount(ramp({ step: 1, scaleEffective: 0.25, liveRiskPct: 0.125, intendedRiskPct: null }), switches());

    const card = await screen.findByRole('region', { name: 'Ramp step' });
    expect(within(card).getByText(/Intended risk not known per trade/)).toBeInTheDocument();
    expect(screen.getByText('S1 is the lowest step.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Lower the step' })).toBeDisabled();
  });

  it('lists every gate with its state as a word and never a digit for a gate that cannot be read', async () => {
    mount(
      ramp({
        step: 1,
        scaleEffective: 0.25,
        liveRiskPct: 0.125,
        nextStep: 2,
        sessions: 7,
        gates: [
          gate('G1_sessions', 'insufficient', { number: 7, threshold: 20, n: 7 }),
          gate('G3_slippage', 'unavailable', { number: null, n: null, reason: 'no reference' }),
          gate('G6_capture', 'pass'),
        ],
      }),
      switches(),
    );

    const table = await screen.findByRole('table', { name: 'Evidence gates' });
    const rows = within(table).getAllByRole('row');
    expect(within(rows[1]!).getByText(/Not enough evidence/)).toBeInTheDocument();
    expect(within(rows[2]!).getByText(/Cannot be read/)).toBeInTheDocument();
    expect(within(rows[2]!).getAllByText('not known').length).toBeGreaterThan(0);
    expect(within(rows[3]!).getByText(/Pass/)).toBeInTheDocument();
    expect(screen.getByText('7 sessions at this step.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Move to S2' })).toBeDisabled();
    expect(screen.getByText(/Move to S2 is unavailable: not every evidence gate passes/)).toBeInTheDocument();
  });

  it('raises the step with the gates passing, a reason and the password typed again', async () => {
    const user = userEvent.setup();
    const posts = mount(
      ramp({
        step: 1,
        scaleEffective: 0.25,
        liveRiskPct: 0.125,
        nextStep: 2,
        eligible: true,
        gates: [gate('G1_sessions', 'pass')],
      }),
      switches(),
    );

    await user.click(await screen.findByRole('button', { name: 'Move to S2' }));
    await user.type(await screen.findByLabelText(/Reason/), 'evidence read');
    await user.type(screen.getByLabelText(/Your password for this move/), 'secret-pass');
    await user.click(screen.getByRole('button', { name: 'Save step' }));

    await waitFor(() => {
      expect(posts).toHaveLength(1);
    });
    expect(posts[0]).toMatchObject({ toStep: 2, reason: 'evidence read', password: 'secret-pass' });
  });

  it('keeps the raise closed under a hold, shows why, and clears the hold with a reason and a password', async () => {
    const user = userEvent.setup();
    const bodies: unknown[] = [];
    server.use(
      http.post('/api/live-trading/ramp/clear-hold', async ({ request }) => {
        bodies.push(await request.json());
        return HttpResponse.json(ramp({ step: 1, scaleEffective: 0.25, liveRiskPct: 0.125 }));
      }),
    );
    mount(
      ramp({
        step: 1,
        scaleEffective: 0.25,
        liveRiskPct: 0.125,
        nextStep: 2,
        eligible: true,
        hold: true,
        holdReason: 'Breach rule: B5',
        holdConditions: [{ code: 'B5', action: 'hold', reason: 'two sessions had a gap' }],
      }),
      switches(),
    );

    expect(await screen.findByRole('button', { name: 'Move to S2' })).toBeDisabled();
    expect(screen.getByText(/a hold blocks raising/)).toBeInTheDocument();
    expect(screen.getByText(/The hold cannot be cleared yet: B5 two sessions had a gap/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Clear hold' }));
    await user.type(await screen.findByLabelText(/Reason/), 'looked at it');
    await user.type(screen.getByLabelText(/Your password for this act/), 'secret-pass');
    await user.click(screen.getByRole('button', { name: 'Clear hold' }));

    await waitFor(() => {
      expect(bodies).toHaveLength(1);
    });
    expect(bodies[0]).toMatchObject({ reason: 'looked at it', password: 'secret-pass' });
  });

  it('shows an unavailable loss as a sentence and a halt with what it does', async () => {
    mount(
      ramp({
        step: 1,
        scaleEffective: 0.25,
        liveRiskPct: 0.125,
        loss: {
          date: '2026-10-28',
          health: 'gap',
          state: 'unavailable',
          netRupees: null,
          lossR: null,
          limitR: 2,
          oneRupees: 125,
          carriedQty: 0,
          openUnmarked: 0,
          note: 'n',
        },
        halts: [
          {
            id: 1,
            accountId: 'a',
            kind: 'daily',
            periodStart: '2026-10-27',
            limitR: 2,
            limitPct: 1.5,
            measuredR: 2.16,
            measuredPct: 0.27,
            measureState: 'value',
            action: 'stop_new',
            openedAt: '2026-10-27T05:00:00Z',
          },
        ],
      }),
      switches(),
    );

    expect(
      await screen.findByText(/cannot be measured \(capture gap\); that is not the same as no loss/),
    ).toBeInTheDocument();
    const halts = screen.getByRole('table', { name: 'Halts' });
    expect(within(halts).getByText('2.16 R of 2.00 R (measured)')).toBeInTheDocument();
    expect(within(halts).getByText('no new entries')).toBeInTheDocument();
  });

  it('records the exit confirmation only from S2 and says the report decides', async () => {
    mount(
      ramp({ step: 2, scaleEffective: 0.5, liveRiskPct: 0.25, nextStep: 3, eligible: true, exitConfirmed: false }),
      switches(),
    );

    expect(await screen.findByRole('button', { name: 'Record exit confirmation' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Move to S3' })).toBeDisabled();
    expect(screen.getByText(/S3 needs your recorded exit confirmation first/)).toBeInTheDocument();
  });

  it('holds the buttons back with the reason when writing is switched off', async () => {
    mount(ramp({ writesEnabled: false }), switches({ writesEnabled: false }));

    expect(await screen.findByRole('button', { name: 'Set the first step' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Change the switches' })).toBeDisabled();
    expect(screen.getAllByText(/Live__Writes__Enabled/).length).toBeGreaterThan(0);
  });

  it('sets S1 with a reason and the password typed again, then clears the password', async () => {
    const user = userEvent.setup();
    const posts = mount(ramp(), switches());

    await user.click(await screen.findByRole('button', { name: 'Set the first step' }));
    await user.type(await screen.findByLabelText(/Reason/), 'start small');
    await user.type(screen.getByLabelText(/Your password for this move/), 'secret-pass');
    await user.click(screen.getByRole('button', { name: 'Save step' }));

    await waitFor(() => {
      expect(posts).toHaveLength(1);
    });
    expect(posts[0]).toMatchObject({ toStep: 1, reason: 'start small', password: 'secret-pass' });
    expect(await screen.findByText(/Saved\. It takes effect on the next plan/)).toBeInTheDocument();
  });

  it('shows a wrong password as an answer and keeps the dialog open', async () => {
    const user = userEvent.setup();
    mount(ramp(), switches(), () =>
      HttpResponse.json({ title: 'Wrong', code: 'auth.invalid_credentials' }, { status: 401 }),
    );

    await user.click(await screen.findByRole('button', { name: 'Set the first step' }));
    await user.type(await screen.findByLabelText(/Reason/), 'x');
    await user.type(screen.getByLabelText(/Your password for this move/), 'nope');
    await user.click(screen.getByRole('button', { name: 'Save step' }));

    expect(await screen.findByText('The password was not accepted. Nothing was saved.')).toBeInTheDocument();
    expect(screen.getByLabelText(/Your password for this move/)).toHaveValue('');
  });

  it('changes the switches in a dialog that sends the row it was loaded at', async () => {
    const user = userEvent.setup();
    const bodies: unknown[] = [];
    server.use(
      http.put('/api/live-trading/settings', async ({ request }) => {
        bodies.push(await request.json());
        return HttpResponse.json(switches({ id: 1, autoApproveTickets: true }));
      }),
    );
    mount(ramp(), switches());

    await user.click(await screen.findByRole('button', { name: 'Change the switches' }));
    await user.click(await screen.findByRole('switch', { name: /Approve live tickets by rule/ }));
    await user.type(screen.getByLabelText(/Reason/), 'try it');
    await user.type(screen.getByLabelText(/Your password for this change/), 'secret-pass');
    await user.click(screen.getByRole('button', { name: 'Save switches' }));

    await waitFor(() => {
      expect(bodies).toHaveLength(1);
    });
    expect(bodies[0]).toMatchObject({
      autoApproveTickets: true,
      autoRampUp: false,
      expectedSettingsId: 0,
      password: 'secret-pass',
    });
  });
});
