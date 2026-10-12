import { screen } from '@testing-library/react';
import { renderWithProviders } from '@/test/render';
import { TopBar } from './TopBar';

describe('Top bar', () => {
  afterEach(() => {
    localStorage.clear();
  });

  it('shows the title and every control: menu, search, accounts, notifications, colour mode and the account menu', async () => {
    renderWithProviders(<TopBar title="Today" onMenu={() => undefined} />);

    expect(screen.getByRole('heading', { name: 'Today' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Open navigation' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Search or jump to' })).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: /^Accounts:/ })).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: /^Notifications/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Toggle colour mode' })).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Account menu, Test Owner' })).toBeInTheDocument();
  });

  it('keeps the controls in the prototype order', async () => {
    renderWithProviders(<TopBar title="Today" onMenu={() => undefined} />);
    await screen.findByRole('button', { name: /^Notifications/ });
    await screen.findByRole('button', { name: /^Account menu/ });
    const order = screen
      .getAllByRole('button')
      .map((b) => b.getAttribute('aria-label') ?? '')
      .filter(Boolean);
    const at = (prefix: string) => order.findIndex((name) => name.startsWith(prefix));
    expect(at('Open navigation')).toBeLessThan(at('Search or jump to'));
    expect(at('Search or jump to')).toBeLessThan(at('Accounts:'));
    expect(at('Accounts:')).toBeLessThan(at('Notifications'));
    expect(at('Notifications')).toBeLessThan(at('Toggle colour mode'));
    expect(at('Toggle colour mode')).toBeLessThan(at('Account menu'));
  });

  it('says in the accounts control which account needs reconnecting', async () => {
    renderWithProviders(<TopBar title="Today" onMenu={() => undefined} />);

    // The default accounts: one connected, one never connected.
    expect(
      await screen.findByRole('button', {
        name: /^Accounts: 2 accounts · 1 needs connecting.*Test Dhan Swing: not connected/,
      }),
    ).toBeInTheDocument();
  });
});
