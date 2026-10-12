import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildAccountDetail, buildProblem, ids, summaryOf } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { AccountsPage } from './components/AccountsPage';

function renderPage(path = '/accounts') {
  return renderRoutes([{ path: '/accounts', element: <AccountsPage /> }], { initialPath: path });
}

describe('AccountsPage — list and detail', () => {
  it('lists accounts with masked client id, role chips and an honest token state', async () => {
    renderPage();

    const list = await screen.findByRole('list', { name: 'Accounts' });
    const main = within(list).getByRole('link', { name: /Test Dhan Main/ });
    expect(main).toHaveAttribute('aria-current', 'true');
    expect(within(main).getByText('Dhan · TEST••90')).toBeInTheDocument();
    expect(within(main).getByText('Data · primary')).toBeInTheDocument();
    expect(within(main).getByText('Trading · paper')).toBeInTheDocument();
    expect(within(main).getByText('Token: Connected')).toBeInTheDocument();
    expect(within(main).getByText('Holdings value: —')).toBeInTheDocument();
    const swing = within(list).getByRole('link', { name: /Test Dhan Swing/ });
    expect(within(swing).getByText('Data · standby')).toBeInTheDocument();
    expect(within(swing).getByText('Trading · manual live')).toBeInTheDocument();
    expect(within(swing).getByText('Token: Not connected')).toBeInTheDocument();
  });

  it('summarises where data comes from and where allocations go', async () => {
    renderPage();

    expect(await screen.findByText(/Market data comes from/)).toHaveTextContent(
      'Market data comes from Test Dhan Main (primary Data); Test Dhan Swing is standby. Trading allocations go to Test Dhan Main, Test Dhan Swing; portfolio covers 1 account.',
    );
  });

  it('warns when there is no primary Data account', async () => {
    const noPrimary = buildAccountDetail({
      roles: {
        data: { enabled: false, primary: false, standby: false },
        trading: { enabled: true, mode: 'paper', profile: 'Standard' },
        portfolio: { enabled: false },
      },
    });
    server.use(http.get('/api/accounts', () => HttpResponse.json([summaryOf(noPrimary)])));

    renderPage();

    expect(await screen.findByText('No primary Data account')).toBeInTheDocument();
  });

  it('shows an empty state with an Add account action', async () => {
    server.use(http.get('/api/accounts', () => HttpResponse.json([])));
    renderPage();

    expect(await screen.findByRole('heading', { name: 'No accounts yet' })).toBeInTheDocument();
    expect(screen.getByText('No primary Data account')).toBeInTheDocument();
    await userEvent.click(screen.getAllByRole('button', { name: 'Add account' })[1]!);
    expect(await screen.findByRole('dialog', { name: 'Add Dhan account' })).toBeInTheDocument();
  });

  it('shows an error when the list cannot be loaded and lets the owner retry', async () => {
    server.use(http.get('/api/accounts', () => HttpResponse.json({ title: 'Service unavailable' }, { status: 503 })));
    renderPage();

    expect(await screen.findByText('Service unavailable')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  it('shows the overview with money tiles as "—" and the reason', async () => {
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Test Dhan Main' })).toBeInTheDocument();
    expect(screen.getByText('Holdings value')).toBeInTheDocument();
    expect(
      screen.getByText(/Available once the broker adapter and portfolio sync are built \(P1-04, P2\)/),
    ).toBeInTheDocument();
    expect(screen.getByText('API key: set · updated 9 Oct 2026')).toBeInTheDocument();
    expect(screen.getByText('API secret: set · updated 9 Oct 2026')).toBeInTheDocument();
    expect(screen.getByText('API key valid until 9 Oct 2027')).toBeInTheDocument();
    expect(screen.getByText(/paper · Standard profile · capital ₹2,00,000 · swing/)).toBeInTheDocument();
    expect(screen.getByText('Included in portfolio totals')).toBeInTheDocument();
    expect(screen.queryByText(/history from/)).not.toBeInTheDocument();
  });

  it('explains disabled actions instead of hiding them', async () => {
    renderPage();

    const sync = await screen.findByRole('button', { name: 'Sync now' });
    expect(sync).toBeDisabled();
    expect(screen.getByText(/Syncing needs the broker adapter and portfolio sync \(P1-04, P2\)/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('tab', { name: 'Data' }));
    expect(screen.getByRole('button', { name: 'Simulate failover' })).toBeDisabled();
    expect(screen.getByText(/Failover testing needs the market-data services \(P1-12\)/)).toBeInTheDocument();
  });

  it('switches tabs and keeps the tab and account in the URL', async () => {
    const { router } = renderPage();
    await screen.findByRole('heading', { name: 'Test Dhan Main' });

    await userEvent.click(screen.getByRole('tab', { name: 'Trading' }));
    expect(router.state.location.search).toBe('?tab=trading');
    expect(await screen.findByRole('form', { name: 'Trading settings' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('link', { name: /Test Dhan Swing/ }));
    expect(router.state.location.search).toContain(`account=${ids.swing}`);
    expect(router.state.location.search).toContain('tab=trading');
    expect(await screen.findByRole('heading', { name: 'Test Dhan Swing' })).toBeInTheDocument();
    expect(await screen.findByLabelText(/^Risk profile/)).toHaveDisplayValue('Conservative');
  });

  it('opens the tab named in the URL', async () => {
    renderPage(`/accounts?account=${ids.swing}&tab=activity`);

    expect(await screen.findByRole('tab', { name: 'Activity', selected: true })).toBeInTheDocument();
    expect(await screen.findByRole('table', { name: 'Account activity' })).toBeInTheDocument();
  });

  it('shows the activity log newest first with readable event names', async () => {
    renderPage('/accounts?tab=activity');

    const table = await screen.findByRole('table', { name: 'Account activity' });
    const rows = within(table).getAllByRole('row');
    expect(rows).toHaveLength(3);
    expect(within(rows[1]!).getByText('Trading updated')).toBeInTheDocument();
    expect(within(rows[2]!).getByText('Account created')).toBeInTheDocument();
    expect(within(rows[1]!).getByText('Trading settings changed: paper')).toBeInTheDocument();
  });

  it('says so when there is no activity', async () => {
    server.use(http.get('/api/accounts/:id/activity', () => HttpResponse.json([])));
    renderPage('/accounts?tab=activity');

    expect(await screen.findByText('No activity yet.')).toBeInTheDocument();
  });

  it('shows an error when the detail cannot be loaded', async () => {
    server.use(
      http.get('/api/accounts/:id', () =>
        problemResponse(buildProblem('accounts.not_found', 404, { title: 'Account not found.' }), 404),
      ),
    );
    renderPage();

    expect(await screen.findByText('Account not found.')).toBeInTheDocument();
  });

  it('shows secrets of an account from before the vault as not set', async () => {
    renderPage(`/accounts?account=${ids.swing}`);

    expect(await screen.findByText('API key: not set')).toBeInTheDocument();
    expect(screen.getByText('API secret: not set')).toBeInTheDocument();
  });

  it('is only about accounts: no role matrix or risk profile views here', async () => {
    renderPage('/accounts?view=profiles');

    expect(await screen.findByRole('heading', { name: 'Test Dhan Main' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Role matrix' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Risk profiles' })).not.toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Role matrix' })).not.toBeInTheDocument();
    expect(screen.queryByRole('article', { name: /Risk profile/ })).not.toBeInTheDocument();
  });
});
