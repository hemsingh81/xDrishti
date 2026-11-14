import { screen } from '@testing-library/react';
import { renderRoutes } from '@/test/render';
import { SettingsPage } from './components/SettingsPage';

vi.mock('./components/EventFilterCard', () => {
  throw new Error('chunk failed');
});

describe('Settings page, a chunk that fails to load', () => {
  it('keeps the failure inside the tab with a Reload button', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    renderRoutes([{ path: '/settings', element: <SettingsPage /> }], { initialPath: '/settings?tab=selection' });

    expect(await screen.findByText(/The event filter could not load/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Selection', selected: true })).toBeInTheDocument();
  });
});
