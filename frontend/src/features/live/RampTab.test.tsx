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
  gatesBuilt: false,
  intendedRiskPct: 0.5,
  liveRiskPct: null,
  writesEnabled: true,
  history: [],
  notice: 'x',
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
  });

  it('shows the live risk at the step and never a digit for a value that is not known', async () => {
    mount(ramp({ step: 1, scaleEffective: 0.25, liveRiskPct: 0.125, intendedRiskPct: null }), switches());

    const card = await screen.findByRole('region', { name: 'Ramp step' });
    expect(within(card).getByText(/Intended risk not known per trade/)).toBeInTheDocument();
    expect(screen.getByText('S1 is the lowest step.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Lower the step' })).toBeDisabled();
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
