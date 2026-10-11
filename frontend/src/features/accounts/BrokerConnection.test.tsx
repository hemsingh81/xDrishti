import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { AccountDetail } from '@/shared/api';
import { externalNavigation } from '@/shared/lib/externalNavigation';
import {
  buildAccountDetail,
  buildConnected,
  buildConnection,
  buildSwingAccount,
  ids,
  summaryOf,
  withConnection,
} from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { createStepUpGate, dhanAuthorizeUrl, problemResponse, server } from '@/test/server';
import { AccountsPage } from './components/AccountsPage';

function renderAt(path: string) {
  return renderRoutes([{ path: '/accounts', element: <AccountsPage /> }], { initialPath: path });
}

const renderTab = (accountId: string = ids.main) => renderAt(`/accounts?account=${accountId}&tab=connection`);

/** Serves these accounts (list and detail) instead of the defaults. */
function serveAccounts(...accounts: AccountDetail[]) {
  server.use(
    http.get('/api/accounts', () => HttpResponse.json(accounts.map(summaryOf))),
    http.get('/api/accounts/:id', ({ params }) => HttpResponse.json(accounts.find((a) => a.id === params.id))),
  );
}

async function confirmPassword() {
  const dialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
  await userEvent.type(within(dialog).getByLabelText(/Password/), 'correct-password');
  await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));
}

const section = () => screen.findByRole('region', { name: 'Broker connection' });
const mockNavigation = () => vi.spyOn(externalNavigation, 'assign').mockImplementation(() => undefined);

describe('Broker connection — status', () => {
  it('shows the status, expiry, broker user and history of a connected account', async () => {
    renderTab();

    const region = await section();
    expect(within(region).getByText('Connected')).toBeInTheDocument();
    expect(within(region).getByText(/^valid until \d{2}:\d{2}, \d{1,2} \w+$/)).toBeInTheDocument();
    expect(within(region).getByText('TEST OWNER')).toBeInTheDocument();
    expect(within(region).getByText('TEST••90')).toBeInTheDocument();
    expect(within(region).queryByText(/Token source|Last checked/)).not.toBeInTheDocument();
    expect(within(region).getByText('none')).toBeInTheDocument();
    // The header repeats the status chip.
    const header = screen.getByRole('region', { name: 'Account Test Dhan Main' });
    expect(within(header).getAllByText('Connected').length).toBeGreaterThan(1);
  });

  it('shows "Not connected" and offers only connecting for an account without a token', async () => {
    renderTab(ids.swing);

    const region = await section();
    expect(within(region).getByText('Not connected')).toBeInTheDocument();
    expect(within(region).getByText('never')).toBeInTheDocument();
    expect(within(region).getByRole('button', { name: 'Connect with Dhan' })).toBeInTheDocument();
    expect(within(region).queryByRole('button', { name: 'Disconnect' })).not.toBeInTheDocument();
  });

  it('shows the last error of a failed connection', async () => {
    serveAccounts(
      withConnection(
        buildAccountDetail(),
        buildConnected({ status: 'expired', lastError: 'Dhan did not answer in 15 s.' }),
      ),
    );
    renderTab();

    const region = await section();
    expect(within(region).getByText('Dhan did not answer in 15 s.')).toBeInTheDocument();
  });

  it('shows the token health in the accounts list from tokenStatus', async () => {
    serveAccounts(
      withConnection(buildAccountDetail(), buildConnected({ status: 'expired' })),
      withConnection(buildSwingAccount(), buildConnection()),
    );
    renderAt('/accounts');

    const list = await screen.findByRole('list', { name: 'Accounts' });
    expect(within(list).getByText('Token: Expired')).toBeInTheDocument();
    expect(within(list).getByText('Token: Not connected')).toBeInTheDocument();
  });

  it('shows the real token status on the Overview', async () => {
    renderAt(`/accounts?account=${ids.swing}`);

    const panel = await screen.findByRole('tabpanel');
    expect(await within(panel).findByText('Not connected')).toBeInTheDocument();
  });
});

describe('Broker connection — connect', () => {
  it('confirms the password, then opens the broker sign-in page in the same tab', async () => {
    const assign = mockNavigation();
    const gate = createStepUpGate();
    let calls = 0;
    server.use(
      gate.handler,
      http.post('/api/accounts/:id/connect', () => {
        calls += 1;
        return (
          gate.require() ?? HttpResponse.json({ authorizeUrl: dhanAuthorizeUrl, expiresAt: '2026-10-11T10:10:00Z' })
        );
      }),
    );
    renderTab();

    await userEvent.click(within(await section()).getByRole('button', { name: 'Connect with Dhan' }));
    await confirmPassword();

    await waitFor(() => {
      expect(assign).toHaveBeenCalledWith(dhanAuthorizeUrl);
    });
    expect(calls).toBe(2); // refused once (password needed), then repeated
  });

  it('cannot connect before the API key and secret are stored, and says so', async () => {
    renderTab(ids.swing);

    const button = within(await section()).getByRole('button', { name: 'Connect with Dhan' });
    expect(button).toBeDisabled();
    expect(button).toHaveAccessibleDescription(/Store the API key and secret first/);
  });

  it.each([
    [
      problemResponse(
        { title: 'Enter the Dhan API key and secret first.', code: 'accounts.app_not_configured', status: 409 },
        409,
      ),
      'Store the API key and secret (Broker app → Replace secrets) before connecting.',
    ],
    [
      problemResponse(
        { title: 'Used up.', code: 'accounts.consent_limit', status: 429, retryAfterSeconds: 11_341 },
        429,
      ),
      "Dhan allows 25 sign-ins a day and today's are used up. Try again in 3 h 10 min.",
    ],
  ])('explains a refused start without leaving the page (%#)', async (response, message) => {
    const assign = mockNavigation();
    server.use(http.post('/api/accounts/:id/connect', () => response.clone()));
    renderTab();

    await userEvent.click(within(await section()).getByRole('button', { name: 'Connect with Dhan' }));

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(assign).not.toHaveBeenCalled();
  });
});

describe('Broker connection — return from the broker', () => {
  it('announces the connection by the broker user name and cleans the URL', async () => {
    let detailCalls = 0;
    server.use(
      http.get('/api/accounts/:id', ({ params }) => {
        detailCalls += 1;
        return HttpResponse.json(params.id === ids.main ? buildAccountDetail() : buildSwingAccount());
      }),
    );
    const { router } = renderAt(`/accounts?account=${ids.main}&connected=1`);

    expect(await screen.findByText('Connected to Dhan as TEST OWNER.')).toBeInTheDocument();
    expect(router.state.location.search).toBe(`?account=${ids.main}&tab=connection`);
    expect(await section()).toBeInTheDocument();
    expect(detailCalls).toBeGreaterThan(0);
  });

  it('shows the mapped message for a failed sign-in and removes it from the URL', async () => {
    const { router } = renderAt(`/accounts?account=${ids.swing}&connectError=accounts.connect_mismatch`);

    expect(await screen.findByText(/You signed in to a different Dhan account than this one/)).toBeInTheDocument();
    expect(router.state.location.search).toBe(`?account=${ids.swing}&tab=connection`);
    expect(await screen.findByRole('link', { name: /Test Dhan Swing/ })).toHaveAttribute('aria-current', 'true');
  });

  it('never repeats an unknown code from the URL', async () => {
    const { router } = renderAt('/accounts?connectError=%3Cb%3Ecall-this-number%3C%2Fb%3E');

    expect(await screen.findByText('Connecting to Dhan failed. Try again.')).toBeInTheDocument();
    expect(screen.queryByText(/call-this-number/)).not.toBeInTheDocument();
    expect(router.state.location.search).toBe('');
  });
});

describe('Broker connection — actions', () => {
  it('offers Reconnect for an expired token, next to Disconnect, and nothing else', async () => {
    serveAccounts(
      withConnection(buildAccountDetail(), buildConnected({ status: 'expired', expiresAt: '2026-01-01T00:00:00Z' })),
    );
    renderTab();

    const region = await section();
    expect(within(region).getByRole('button', { name: 'Reconnect with Dhan' })).toBeEnabled();
    expect(within(region).getByRole('button', { name: 'Disconnect' })).toBeInTheDocument();
    expect(within(region).getAllByRole('button')).toHaveLength(2);
  });

  it('has only Disconnect and Connect for a connected account; no paste, renew or test', async () => {
    renderTab();

    const region = await section();
    expect(within(region).getByRole('button', { name: 'Connect with Dhan' })).toBeInTheDocument();
    expect(within(region).getByRole('button', { name: 'Disconnect' })).toBeInTheDocument();
    for (const name of [/paste/i, /renew/i, /test connection/i]) {
      expect(within(region).queryByRole('button', { name })).not.toBeInTheDocument();
    }
    expect(screen.queryByRole('button', { name: /^Reconnect with/ })).not.toBeInTheDocument();
  });

  it('disconnects after confirmation and the password', async () => {
    const gate = createStepUpGate();
    const deleted: string[] = [];
    server.use(
      gate.handler,
      http.delete('/api/accounts/:id/token', ({ params }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        deleted.push(String(params.id));
        return HttpResponse.json(withConnection(buildAccountDetail(), buildConnection()));
      }),
    );
    renderTab();

    await userEvent.click(within(await section()).getByRole('button', { name: 'Disconnect' }));
    const confirm = await screen.findByRole('dialog', { name: 'Disconnect Test Dhan Main?' });
    expect(within(confirm).getByText(/The stored access token is deleted/)).toBeInTheDocument();
    await userEvent.click(within(confirm).getByRole('button', { name: 'Disconnect' }));
    await confirmPassword();

    expect(await screen.findByText('Test Dhan Main was disconnected from Dhan.')).toBeInTheDocument();
    expect(deleted).toEqual([ids.main]);
    expect(within(await section()).getByText('Not connected')).toBeInTheDocument();
  });
});
