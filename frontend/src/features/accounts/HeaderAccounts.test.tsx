import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { Suspense } from 'react';
import type { AccountDetail } from '@/shared/api';
import { externalNavigation } from '@/shared/lib/externalNavigation';
import {
  buildAccountDetail,
  buildConnected,
  buildConnection,
  buildSwingAccount,
  summaryOf,
  withConnection,
} from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { createStepUpGate, dhanAuthorizeUrl, problemResponse, server } from '@/test/server';
import { AccountsPage } from './components/AccountsPage';
import { HeaderAccounts } from './index';

function renderHeader() {
  return renderRoutes(
    [
      {
        path: '/',
        element: (
          <Suspense fallback={null}>
            <HeaderAccounts />
          </Suspense>
        ),
      },
      { path: '/accounts', element: <p>Accounts page</p> },
    ],
    { initialPath: '/' },
  );
}

function serveAccounts(...accounts: AccountDetail[]) {
  server.use(http.get('/api/accounts', () => HttpResponse.json(accounts.map(summaryOf))));
}

const open = async (name: RegExp | string) => {
  await userEvent.click(await screen.findByRole('button', { name }));
  return screen.findByRole('dialog', { name: 'Accounts' });
};

/** The not-connected swing account with its API key and secret stored, so it can be connected. */
const swingWithSecrets = () =>
  buildSwingAccount({
    secrets: [
      { name: 'apiKey', set: true, updatedAt: '2026-10-09T09:00:00Z' },
      { name: 'apiSecret', set: true, updatedAt: '2026-10-09T09:00:00Z' },
    ],
  });

describe('Header accounts', () => {
  it('summarises the accounts and lists each with its status; only the unconnected one needs connecting', async () => {
    serveAccounts(buildAccountDetail(), swingWithSecrets());
    renderHeader();

    const menu = await open(/2 accounts · 1 needs connecting/);

    const list = within(menu).getByRole('list', { name: 'Account connections' });
    const connected = within(list).getAllByRole('listitem')[0]!;
    expect(within(connected).getByText('Test Dhan Main')).toBeInTheDocument();
    expect(within(connected).getByText('Connected')).toBeInTheDocument();
    expect(within(connected).getByText(/^valid until/)).toBeInTheDocument();
    expect(within(connected).queryByRole('button')).not.toBeInTheDocument();
    const swing = within(list).getAllByRole('listitem')[1]!;
    expect(within(swing).getByText('Not connected')).toBeInTheDocument();
    expect(within(swing).getByRole('button', { name: 'Connect Test Dhan Swing' })).toBeInTheDocument();
    expect(within(menu).getByRole('link', { name: 'Manage accounts' })).toHaveAttribute('href', '/accounts');
  });

  it('says all connected when nothing needs connecting', async () => {
    serveAccounts(buildAccountDetail());
    renderHeader();

    const menu = await open(/1 account · all connected/);

    expect(within(menu).queryByRole('button', { name: /Connect|Reconnect/ })).not.toBeInTheDocument();
  });

  it('offers Reconnect once a token has expired', async () => {
    serveAccounts(withConnection(buildAccountDetail(), buildConnected({ status: 'expired' })));
    renderHeader();

    const menu = await open(/1 account · 1 needs connecting/);

    expect(within(menu).getByText('Expired')).toBeInTheDocument();
    expect(within(menu).getByRole('button', { name: 'Reconnect Test Dhan Main' })).toBeInTheDocument();
  });

  it('names the accounts that need reconnecting in the accessible name, and in a tooltip with the state in words', async () => {
    serveAccounts(
      withConnection(buildAccountDetail(), buildConnected({ status: 'expired' })),
      buildSwingAccount(),
      buildAccountDetail({ id: '00000000-0000-4000-8000-0000000000aa', name: 'Fine Account' }),
    );
    renderHeader();

    const trigger = await screen.findByRole('button', {
      name: 'Accounts: 3 accounts · 2 needs connecting — needs reconnecting: Test Dhan Main: expired; Test Dhan Swing: not connected',
    });

    await userEvent.hover(trigger);
    expect(await screen.findByRole('tooltip')).toHaveTextContent(
      'Test Dhan Main: expired; Test Dhan Swing: not connected',
    );
  });

  it('adds nothing to the accessible name when every account is connected', async () => {
    serveAccounts(buildAccountDetail());
    renderHeader();

    const trigger = await screen.findByRole('button', { name: /1 account · all connected/ });

    expect(trigger).toHaveAccessibleName('Accounts: 1 account · all connected');
  });

  it('tells the owner what to do at the top of the menu, and what is wrong in each row', async () => {
    serveAccounts(withConnection(buildAccountDetail(), buildConnected({ status: 'expired' })), swingWithSecrets());
    renderHeader();

    const menu = await open(/needs connecting/);

    expect(within(menu).getByText('Reconnect to keep holdings and data up to date.')).toBeInTheDocument();
    const [expired, notConnected] = within(menu).getAllByRole('listitem');
    expect(within(expired!).getByText('Expired')).toBeInTheDocument();
    expect(within(expired!).getByText('The Dhan session has expired — reconnect to continue.')).toBeInTheDocument();
    expect(within(expired!).getByRole('button', { name: 'Reconnect Test Dhan Main' })).toBeInTheDocument();
    expect(
      within(notConnected!).getByText('Not signed in to Dhan — connect to import holdings and data.'),
    ).toBeInTheDocument();
    expect(within(notConnected!).getByRole('button', { name: 'Connect Test Dhan Swing' })).toBeInTheDocument();
  });

  it('asks for the API key and secret first when they are not stored', async () => {
    serveAccounts(buildSwingAccount());
    renderHeader();

    const menu = await open(/needs connecting/);

    expect(within(menu).getByText('Add the API key & secret, then connect to Dhan.')).toBeInTheDocument();
  });

  it('shows neither the hint nor next steps when everything is connected', async () => {
    serveAccounts(buildAccountDetail());
    renderHeader();

    const menu = await open(/all connected/);

    expect(within(menu).queryByText(/Reconnect to keep holdings/)).not.toBeInTheDocument();
    expect(within(menu).queryByText(/reconnect to continue|connect to import/)).not.toBeInTheDocument();
  });

  it('turns to needing a reconnect when a connected token passes its expiry, without refetching', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      let fetches = 0;
      server.use(
        http.get('/api/accounts', () => {
          fetches += 1;
          const expiresAt = new Date(Date.now() + 60_000).toISOString();
          return HttpResponse.json([summaryOf(buildAccountDetail({ connection: buildConnected({ expiresAt }) }))]);
        }),
      );
      renderHeader();
      expect(await screen.findByRole('button', { name: /1 account · all connected/ })).toBeInTheDocument();

      await act(async () => {
        await vi.advanceTimersByTimeAsync(90_000);
      });

      expect(
        await screen.findByRole('button', { name: /1 account · 1 needs connecting.*Test Dhan Main: expired/ }),
      ).toBeInTheDocument();
      expect(fetches).toBe(1);
    } finally {
      vi.useRealTimers();
    }
  });

  it('starts the Dhan sign-in from the header after the password, then opens its page', async () => {
    const assign = vi.spyOn(externalNavigation, 'assign').mockImplementation(() => undefined);
    const gate = createStepUpGate();
    const connects: string[] = [];
    serveAccounts(buildAccountDetail(), swingWithSecrets());
    server.use(
      gate.handler,
      http.post('/api/accounts/:id/connect', ({ params }) => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        connects.push(String(params.id));
        return HttpResponse.json({ authorizeUrl: dhanAuthorizeUrl, expiresAt: '2026-10-11T10:10:00Z' });
      }),
    );
    renderHeader();
    const menu = await open(/needs connecting/);

    await userEvent.click(within(menu).getByRole('button', { name: 'Connect Test Dhan Swing' }));
    const passwordDialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.type(within(passwordDialog).getByLabelText(/Password/), 'correct-password');
    await userEvent.click(within(passwordDialog).getByRole('button', { name: 'Confirm' }));

    await waitFor(() => {
      expect(assign).toHaveBeenCalledWith(dhanAuthorizeUrl);
    });
    expect(connects).toHaveLength(1);
  });

  it('shows why the sign-in could not start, without leaving the page', async () => {
    const assign = vi.spyOn(externalNavigation, 'assign').mockImplementation(() => undefined);
    serveAccounts(swingWithSecrets());
    server.use(
      http.post('/api/accounts/:id/connect', () =>
        problemResponse({ title: 'Dhan did not answer.', code: 'accounts.broker_error', status: 502 }, 502),
      ),
    );
    renderHeader();
    const menu = await open(/needs connecting/);

    await userEvent.click(within(menu).getByRole('button', { name: 'Connect Test Dhan Swing' }));

    expect(await within(menu).findByRole('alert')).toHaveTextContent('Dhan did not answer.');
    expect(assign).not.toHaveBeenCalled();
  });

  it('points to the Connection tab when the API key and secret are not stored yet', async () => {
    serveAccounts(buildSwingAccount());
    const { router } = renderHeader();
    const menu = await open(/needs connecting/);

    expect(within(menu).queryByRole('button', { name: /Connect/ })).not.toBeInTheDocument();
    await userEvent.click(within(menu).getByRole('link', { name: /Add API key & secret/ }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/accounts');
    });
    expect(router.state.location.search).toBe(`?account=${buildSwingAccount().id}&tab=connection`);
  });

  it('offers to add an account when there is none', async () => {
    serveAccounts();
    renderHeader();

    const menu = await open(/No accounts yet/);

    expect(within(menu).getByRole('link', { name: 'Add account' })).toHaveAttribute('href', '/accounts');
    expect(within(menu).queryByRole('link', { name: 'Manage accounts' })).not.toBeInTheDocument();
  });

  it('stays quiet while loading: the button is there but disabled', async () => {
    server.use(http.get('/api/accounts', () => new Promise(() => undefined)));
    renderHeader();

    expect(await screen.findByRole('button', { name: /Accounts: Accounts/ })).toBeDisabled();
  });

  it('says so in small text when the accounts cannot be loaded, and retries', async () => {
    server.use(http.get('/api/accounts', () => HttpResponse.json({ title: 'Service unavailable' }, { status: 503 })));
    renderHeader();
    const menu = await open(/Accounts unavailable/);

    expect(within(menu).getByRole('alert')).toHaveTextContent('Could not load the accounts.');
    serveAccounts(buildAccountDetail());
    await userEvent.click(within(menu).getByRole('button', { name: 'Try again' }));

    expect(await within(menu).findByText('Test Dhan Main')).toBeInTheDocument();
  });

  it('follows changes made on the Accounts page, such as disconnecting', async () => {
    let disconnected = false;
    const gate = createStepUpGate();
    server.use(
      gate.handler,
      http.get('/api/accounts', () =>
        HttpResponse.json([
          summaryOf(disconnected ? withConnection(buildAccountDetail(), buildConnection()) : buildAccountDetail()),
        ]),
      ),
      http.delete('/api/accounts/:id/token', () => {
        const denied = gate.require();
        if (denied) {
          return denied;
        }
        disconnected = true;
        return HttpResponse.json(withConnection(buildAccountDetail(), buildConnection()));
      }),
    );
    renderRoutes(
      [
        {
          path: '/accounts',
          element: (
            <Suspense fallback={null}>
              <HeaderAccounts />
              <AccountsPage />
            </Suspense>
          ),
        },
      ],
      { initialPath: `/accounts?account=${buildAccountDetail().id}&tab=connection` },
    );
    expect(await screen.findByRole('button', { name: /1 account · all connected/ })).toBeInTheDocument();

    await userEvent.click(await screen.findByRole('button', { name: 'Disconnect' }));
    const confirm = await screen.findByRole('dialog', { name: 'Disconnect Test Dhan Main?' });
    await userEvent.click(within(confirm).getByRole('button', { name: 'Disconnect' }));
    const passwordDialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.type(within(passwordDialog).getByLabelText(/Password/), 'correct-password');
    await userEvent.click(within(passwordDialog).getByRole('button', { name: 'Confirm' }));

    expect(await screen.findByRole('button', { name: /1 account · 1 needs connecting/ })).toBeInTheDocument();
  });
});
