import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildAccountDetail, buildSwingAccount, ids } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { createStepUpGate, problemResponse, server } from '@/test/server';
import { AccountsPage } from './components/AccountsPage';

function renderRoles(accountId: string = ids.main) {
  return renderRoutes([{ path: '/accounts', element: <AccountsPage /> }], {
    initialPath: `/accounts?account=${accountId}&tab=roles`,
  });
}

async function confirmPassword() {
  const dialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
  await userEvent.type(within(dialog).getByLabelText(/Password/), 'correct-password');
  await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));
}

describe('Roles tab', () => {
  it('explains that the primary Data account cannot be switched off or made standby', async () => {
    renderRoles();

    expect(
      await screen.findByText(/This is the primary Data account\. Make another account primary/),
    ).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Data' })).toBeDisabled();
    expect(screen.getByRole('radio', { name: 'Standby (automatic failover)' })).toBeDisabled();
    expect(screen.getByRole('radio', { name: 'Primary' })).toBeChecked();
  });

  it('makes a standby account primary through make-primary-data (not through the roles request)', async () => {
    const gate = createStepUpGate();
    const calls: string[] = [];
    server.use(
      gate.handler,
      http.put('/api/accounts/:id/roles', () => {
        calls.push('PUT roles');
        return HttpResponse.json(buildSwingAccount());
      }),
      http.post('/api/accounts/:id/make-primary-data', ({ params }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        calls.push(`make-primary ${String(params.id)}`);
        return HttpResponse.json(buildSwingAccount());
      }),
    );
    renderRoles(ids.swing);

    await userEvent.click(await screen.findByRole('radio', { name: 'Primary' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save roles' }));
    await confirmPassword();

    expect(await screen.findByText(/Roles saved/)).toBeInTheDocument();
    expect(calls).toEqual([`make-primary ${ids.swing}`]);
  });

  it('switches a role off with a roles request that only names the changed role', async () => {
    const gate = createStepUpGate();
    const bodies: unknown[] = [];
    server.use(
      gate.handler,
      http.put('/api/accounts/:id/roles', async ({ request }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        bodies.push(await request.json());
        return HttpResponse.json(buildAccountDetail());
      }),
    );
    renderRoles();

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Trading' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save roles' }));
    await confirmPassword();

    expect(await screen.findByText(/Roles saved/)).toBeInTheDocument();
    expect(bodies).toEqual([{ data: null, trading: { enabled: false }, portfolio: null }]);
  });

  it('says there is nothing to change instead of calling the API', async () => {
    renderRoles();

    await userEvent.click(await screen.findByRole('button', { name: 'Save roles' }));

    expect(await screen.findByText('Nothing to change.')).toBeInTheDocument();
  });

  it('switches Portfolio on right away: the account’s own bucket needs no set-up', async () => {
    const gate = createStepUpGate();
    const bodies: unknown[] = [];
    server.use(
      gate.handler,
      http.put('/api/accounts/:id/roles', async ({ request }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        bodies.push(await request.json());
        return HttpResponse.json(buildAccountDetail());
      }),
    );
    renderRoles(ids.swing);

    const portfolio = await screen.findByRole('checkbox', { name: 'Portfolio' });
    expect(portfolio).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'open Portfolio settings' })).not.toBeInTheDocument();
    await userEvent.click(portfolio);
    await userEvent.click(screen.getByRole('button', { name: 'Save roles' }));
    await confirmPassword();

    expect(await screen.findByText(/Roles saved/)).toBeInTheDocument();
    expect(bodies).toEqual([{ data: null, trading: null, portfolio: { enabled: true } }]);
  });

  it('shows the server message when a change is refused', async () => {
    server.use(
      http.put('/api/accounts/:id/roles', () =>
        problemResponse(
          { title: "'Test Dhan Main' is already the primary Data account.", code: 'accounts.conflict' },
          409,
        ),
      ),
    );
    renderRoles(ids.swing);

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Trading' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save roles' }));

    expect(await screen.findByRole('alert')).toHaveTextContent("'Test Dhan Main' is already the primary Data account.");
  });

  it('lists field messages from the server (e.g. incomplete settings)', async () => {
    server.use(
      http.put('/api/accounts/:id/roles', () =>
        problemResponse(
          {
            title: 'Invalid',
            code: 'accounts.invalid',
            errors: { 'roles.trading': ['Complete the trading settings before switching the role on.'] },
          },
          400,
        ),
      ),
    );
    renderRoles(ids.swing);

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Data' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save roles' }));

    expect(await screen.findByText('Complete the trading settings before switching the role on.')).toBeInTheDocument();
  });
});
