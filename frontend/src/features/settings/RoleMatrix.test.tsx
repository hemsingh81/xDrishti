import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildAccountDetail, buildSwingAccount, ids, summaryOf } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { createStepUpGate, problemResponse, server } from '@/test/server';
import { SettingsPage } from './components/SettingsPage';

function renderMatrix() {
  return renderRoutes([{ path: '/settings', element: <SettingsPage /> }], { initialPath: '/settings?tab=roles' });
}

/** The two test accounts after "Test Dhan Swing" became primary: the old primary is standby. */
const swapped = () => [
  summaryOf(
    buildAccountDetail({
      roles: {
        data: { enabled: true, primary: false, standby: true },
        trading: { enabled: true, mode: 'paper', profile: 'Standard' },
        portfolio: { enabled: true },
      },
    }),
  ),
  summaryOf(
    buildSwingAccount({
      roles: {
        data: { enabled: true, primary: true, standby: false },
        trading: { enabled: true, mode: 'manual_live', profile: 'Conservative' },
        portfolio: { enabled: false },
      },
    }),
  ),
];

describe('Role matrix', () => {
  it('shows every account with its roles, mode and risk profile', async () => {
    renderMatrix();

    const table = await screen.findByRole('table', { name: 'Role matrix' });
    expect(within(table).getByRole('checkbox', { name: 'Data — Test Dhan Main' })).toBeChecked();
    expect(within(table).getByRole('radio', { name: 'Primary data — Test Dhan Main' })).toBeChecked();
    expect(within(table).getByRole('radio', { name: 'Primary data — Test Dhan Swing' })).not.toBeChecked();
    expect(within(table).getByRole('checkbox', { name: 'Portfolio — Test Dhan Swing' })).not.toBeChecked();
    expect(within(table).getByText('manual live')).toBeInTheDocument();
    expect(within(table).getByText('Conservative')).toBeInTheDocument();
  });

  it('keeps Save off until something changed', async () => {
    renderMatrix();

    const save = await screen.findByRole('button', { name: 'Save roles' });
    expect(save).toBeDisabled();
    await userEvent.click(screen.getByRole('checkbox', { name: 'Portfolio — Test Dhan Swing' }));
    expect(save).toBeEnabled();
  });

  it('does not let the primary Data account be unticked', async () => {
    renderMatrix();

    expect(await screen.findByRole('checkbox', { name: 'Data — Test Dhan Main' })).toBeDisabled();
    expect(
      screen.getByText(/The primary Data account cannot be switched off — pick another primary first/),
    ).toBeInTheDocument();
  });

  it('changes the primary through make-primary-data, with one password prompt', async () => {
    const gate = createStepUpGate();
    let accounts = [summaryOf(buildAccountDetail()), summaryOf(buildSwingAccount())];
    const calls: string[] = [];
    server.use(
      gate.handler,
      http.get('/api/accounts', () => HttpResponse.json(accounts)),
      http.post('/api/accounts/:id/make-primary-data', ({ params }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        calls.push(`make-primary ${String(params.id)}`);
        accounts = swapped();
        return HttpResponse.json(buildSwingAccount());
      }),
      http.put('/api/accounts/:id/roles', ({ params }) => {
        calls.push(`roles ${String(params.id)}`);
        return HttpResponse.json(buildAccountDetail());
      }),
    );
    renderMatrix();

    await userEvent.click(await screen.findByRole('radio', { name: 'Primary data — Test Dhan Swing' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save roles' }));
    const dialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'correct-password');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));

    expect(await screen.findByText(/Roles saved/)).toBeInTheDocument();
    // The swap moved the old primary to standby by itself: no roles request for it.
    expect(calls).toEqual([`make-primary ${ids.swing}`]);
    await waitFor(() => {
      expect(screen.getByRole('radio', { name: 'Primary data — Test Dhan Swing' })).toBeChecked();
    });
    expect(screen.getByRole('checkbox', { name: 'Data — Test Dhan Main' })).toBeEnabled();
  });

  it('applies role changes per account with a roles request and reports failures per row', async () => {
    const gate = createStepUpGate();
    const bodies: Record<string, unknown> = {};
    server.use(
      gate.handler,
      http.put('/api/accounts/:id/roles', async ({ request, params }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        bodies[String(params.id)] = await request.json();
        if (params.id === ids.swing) {
          return problemResponse(
            {
              title: 'Invalid',
              code: 'accounts.invalid',
              errors: { 'roles.portfolio': ['Choose a default bucket before switching the role on.'] },
            },
            400,
          );
        }
        return HttpResponse.json(buildAccountDetail());
      }),
    );
    renderMatrix();

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Trading — Test Dhan Main' }));
    await userEvent.click(screen.getByRole('checkbox', { name: 'Portfolio — Test Dhan Swing' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save roles' }));
    const dialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'correct-password');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));

    const swingRow = screen.getByRole('row', { name: /Test Dhan Swing/ });
    expect(
      await within(swingRow).findByText('Choose a default bucket before switching the role on.'),
    ).toBeInTheDocument();
    expect(bodies[ids.main]).toEqual({ data: null, trading: { enabled: false }, portfolio: null });
    expect(bodies[ids.swing]).toEqual({ data: null, trading: null, portfolio: { enabled: true } });
    expect(screen.queryByText(/Roles saved/)).not.toBeInTheDocument();
  });

  it('stops without changes when the password dialog is cancelled', async () => {
    const gate = createStepUpGate();
    let writes = 0;
    server.use(
      gate.handler,
      http.put('/api/accounts/:id/roles', () => {
        writes += 1;
        return gate.require() ?? HttpResponse.json(buildAccountDetail());
      }),
    );
    renderMatrix();

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Portfolio — Test Dhan Swing' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save roles' }));
    const dialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    expect(await screen.findByText(/Password not confirmed/)).toBeInTheDocument();
    expect(writes).toBe(1);
  });
});
