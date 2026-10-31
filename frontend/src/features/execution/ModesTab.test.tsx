import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { setNow } from '@/test/clock';
import { buildLimitsView, buildModeAccount, buildModes, execAccountId } from '@/test/executionFixtures';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { ExecutionPage } from './components/ExecutionPage';
import { recordWrites } from './testing';

recordWrites();

const open = () =>
  renderRoutes([{ path: '/execution', element: <ExecutionPage /> }], { initialPath: '/execution?tab=modes' });
const modes = (account = buildModeAccount()) => {
  server.use(
    http.get('/api/execution/modes', () => HttpResponse.json(buildModes([account]))),
    http.get('/api/execution/limits', () => HttpResponse.json(buildLimitsView())),
  );
};
const request = (iso: string, eligible: string, expires: string) => ({
  id: 'r1',
  toMode: 'assisted',
  requestedAt: iso,
  eligibleFrom: eligible,
  expiresAt: expires,
  evidenceHash: 'e',
});
const card = async () => screen.findByRole('region', { name: 'Test Dhan Main modes' });

describe('Modes tab', () => {
  beforeEach(() => {
    setNow('2026-10-30T04:00:00Z');
  });

  it('shows the ladder, the next gate with each check as a word, and the standing sentence', async () => {
    modes();
    open();
    const c = await card();
    expect(within(c).getByRole('list', { name: 'Mode ladder' })).toHaveTextContent('manual live (current)');
    const gate = within(c).getByRole('region', { name: 'Next gate' });
    expect(gate).toHaveTextContent('manual live to assisted, eligible');
    expect(within(gate).getByText('Pass')).toBeInTheDocument();
    expect(within(gate).getByText('Unavailable')).toBeInTheDocument();
    expect(within(gate).getByText(/Enough sessions in manual_live \(12 of 10\)/)).toBeInTheDocument();
    expect(
      within(c).getByText('Moving up needs your password twice, at least two sessions apart; moving down is instant.'),
    ).toBeInTheDocument();
  });

  it('disables Request promotion unless the gate is eligible', async () => {
    const base = buildModeAccount();
    modes({ ...base, next: base.next && { ...base.next, verdict: 'not_eligible' } });
    open();
    await card();
    expect(screen.getByRole('button', { name: 'Request promotion' })).toBeDisabled();
  });

  it('asks the password and the reason, sends the request, and shows the server refusal as it came', async () => {
    modes();
    let body: unknown;
    server.use(
      http.post(`/api/execution/modes/${execAccountId}/promotion-requests`, async ({ request: r }) => {
        body = await r.json();
        return HttpResponse.json(
          { title: 'Password is wrong', detail: 'The password was not accepted.' },
          { status: 403 },
        );
      }),
    );
    open();
    await card();
    await userEvent.click(screen.getByRole('button', { name: 'Request promotion' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'ready');
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'pw');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Request promotion' }));
    expect(await within(dialog).findByText('The password was not accepted.')).toBeInTheDocument();
    expect(body).toEqual({ toMode: 'assisted', reason: 'ready', password: 'pw' });
  });

  it('keeps Confirm promotion disabled during the cooling-off and counts down from the server time', async () => {
    modes(
      buildModeAccount({ request: request('2026-10-30T03:00:00Z', '2026-10-30T06:00:00Z', '2026-10-31T06:00:00Z') }),
    );
    open();
    const c = await card();
    expect(within(c).getByRole('button', { name: 'Confirm promotion' })).toBeDisabled();
    const timer = within(c).getByRole('timer');
    expect(timer).toHaveTextContent('Cooling-off: confirming is possible in 2h');
    expect(timer).toHaveAttribute('aria-live', 'off');
    expect(within(c).getByRole('button', { name: 'Request promotion' })).toBeDisabled();
  });

  it('enables Confirm promotion after the cooling-off and sends it with the request id', async () => {
    setNow('2026-10-30T07:00:00Z');
    modes(
      buildModeAccount({ request: request('2026-10-30T03:00:00Z', '2026-10-30T06:00:00Z', '2026-10-31T06:00:00Z') }),
    );
    let url = '';
    server.use(
      http.post(`/api/execution/modes/${execAccountId}/promotion-requests/r1/confirm`, ({ request: r }) => {
        url = new URL(r.url).pathname;
        return HttpResponse.json({});
      }),
    );
    open();
    const c = await card();
    await userEvent.click(within(c).getByRole('button', { name: 'Confirm promotion' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'second look');
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'pw');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm promotion' }));
    await vi.waitFor(() => {
      expect(url).toContain('/promotion-requests/r1/confirm');
    });
  });

  it('moves down one step after a confirm dialog and without a password', async () => {
    modes();
    let body: unknown;
    server.use(
      http.post(`/api/execution/modes/${execAccountId}/demotions`, async ({ request: r }) => {
        body = await r.json();
        return HttpResponse.json({});
      }),
    );
    open();
    await card();
    await userEvent.click(screen.getByRole('button', { name: 'Move down one step' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).queryByLabelText(/Password/)).not.toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Move down to paper' }));
    await vi.waitFor(() => {
      expect(body).toMatchObject({ toMode: 'paper' });
    });
  });

  it('offers no move down from paper and says there is no switch or drill control here', async () => {
    const base = buildModeAccount();
    modes({ ...base, effectiveMode: 'paper', configuredMode: 'paper' });
    open();
    await card();
    expect(screen.queryByRole('button', { name: 'Move down one step' })).not.toBeInTheDocument();
    expect(screen.getByText(/execution_enabled: off/)).toBeInTheDocument();
    expect(screen.getByText(/Raising risk waits one closed session/)).toBeInTheDocument();
  });
});
