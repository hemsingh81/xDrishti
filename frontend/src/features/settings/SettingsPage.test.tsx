import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { SettingsPage } from './components/SettingsPage';

function renderSettings(path = '/settings') {
  return renderRoutes([{ path: '/settings', element: <SettingsPage /> }], { initialPath: path });
}

describe('Settings page', () => {
  it('opens on the risk profiles and switches to the account roles, keeping the tab in the URL', async () => {
    const { router } = renderSettings();

    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Risk profiles', selected: true })).toBeInTheDocument();
    const panel = screen.getByRole('tabpanel');
    expect(await within(panel).findByRole('article', { name: 'Risk profile Standard' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('tab', { name: 'Account roles' }));

    expect(router.state.location.search).toBe('?tab=roles');
    expect(await screen.findByRole('table', { name: 'Role matrix' })).toBeInTheDocument();
    expect(screen.queryByRole('article', { name: 'Risk profile Standard' })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('tab', { name: 'Risk profiles' }));
    expect(router.state.location.search).toBe('');
  });

  it('opens the tab named in the URL', async () => {
    renderSettings('/settings?tab=roles');

    expect(await screen.findByRole('tab', { name: 'Account roles', selected: true })).toBeInTheDocument();
    expect(await screen.findByRole('table', { name: 'Role matrix' })).toBeInTheDocument();
  });

  it('says so when there are no accounts to give roles to', async () => {
    server.use(http.get('/api/accounts', () => HttpResponse.json([])));
    renderSettings('/settings?tab=roles');

    expect(await screen.findByText(/No accounts yet — add one on the Accounts page/)).toBeInTheDocument();
  });

  it('shows an error when the accounts cannot be loaded', async () => {
    server.use(http.get('/api/accounts', () => HttpResponse.json({ title: 'Service unavailable' }, { status: 503 })));
    renderSettings('/settings?tab=roles');

    expect(await screen.findByText('Service unavailable')).toBeInTheDocument();
  });
});
