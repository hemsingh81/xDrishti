import { screen } from '@testing-library/react';
import { renderRoutes } from '@/test/render';
import { SettingsPage } from './components/SettingsPage';

vi.mock('./components/ChargeSheetCard', () => {
  throw new Error('chunk failed');
});

describe('Settings page, a chunk that fails to load', () => {
  it('keeps the failure inside the tab with a Reload button', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    renderRoutes([{ path: '/settings', element: <SettingsPage /> }], { initialPath: '/settings?tab=charge-sheet' });

    expect(await screen.findByText(/The confirmation could not load/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Charge sheet', selected: true })).toBeInTheDocument();
  });
});
