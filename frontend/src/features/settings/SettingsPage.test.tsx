import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { buildCatalogItem } from '@/test/settingsCatalogFixtures';
import { SettingsPage } from './components/SettingsPage';

function renderSettings(path = '/settings') {
  return renderRoutes([{ path: '/settings', element: <SettingsPage /> }], { initialPath: path });
}

describe('Settings page', () => {
  it('opens on the five essential settings with no advanced control', async () => {
    renderSettings();

    const list = await screen.findByRole('list', { name: 'Essential settings' });
    expect(within(list).getAllByRole('listitem')).toHaveLength(5);
    for (const name of ['Auto', 'What to suggest', 'Risk profile', 'Charge sheet', 'Failure notices']) {
      expect(within(list).getByRole('heading', { name })).toBeInTheDocument();
    }
    expect(screen.queryByText('Promote models automatically')).not.toBeInTheDocument();
    expect(screen.queryByText('Capital for grading')).not.toBeInTheDocument();
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
    expect(within(list).queryByRole('switch')).not.toBeInTheDocument();
    expect(within(list).getByRole('link', { name: 'Open risk profiles' })).toHaveAttribute(
      'href',
      '/settings?tab=profiles',
    );
    expect(within(list).getByRole('link', { name: 'Open the charge sheet' })).toHaveAttribute(
      'href',
      '/settings?tab=charge-sheet',
    );
    expect(within(list).getAllByText(/Not changeable here yet/)).toHaveLength(3);
  });

  it('marks a value as default or changed', async () => {
    server.use(
      http.get('/api/settings/catalog', () =>
        HttpResponse.json({
          items: [
            buildCatalogItem({ key: 'notices.failure', label: 'Failure notices', value: 'false', default: 'true' }),
            buildCatalogItem({ key: 'auto.enabled', label: 'Auto', value: 'false', default: 'false' }),
          ],
        }),
      ),
    );
    renderSettings();

    expect(await screen.findByText('Off · changed from default')).toBeInTheDocument();
    expect(screen.getByText('Off · default')).toBeInTheDocument();
  });

  it('says so while loading, when empty and when the catalog fails', async () => {
    server.use(http.get('/api/settings/catalog', () => HttpResponse.json({ items: [] })));
    const first = renderSettings();
    expect(await screen.findByText('No settings to show yet.')).toBeInTheDocument();
    first.unmount();

    server.use(
      http.get('/api/settings/catalog', () => HttpResponse.json({ title: 'Service unavailable' }, { status: 503 })),
    );
    renderSettings();
    expect(screen.getByRole('status')).toHaveTextContent('Loading your settings');
    expect(await screen.findByText('Service unavailable')).toBeInTheDocument();
  });

  it('reveals the existing tabs under Advanced settings and hides them again', async () => {
    const { router } = renderSettings();

    await userEvent.click(await screen.findByRole('button', { name: 'Advanced settings' }));

    expect(router.state.location.search).toBe('?tab=profiles');
    expect(await screen.findByRole('tab', { name: 'Risk profiles', selected: true })).toBeInTheDocument();
    expect(await screen.findByRole('article', { name: 'Risk profile Standard' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('tab', { name: 'Account roles' }));
    expect(router.state.location.search).toBe('?tab=roles');
    expect(await screen.findByRole('table', { name: 'Role matrix' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Hide advanced settings' }));
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
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
