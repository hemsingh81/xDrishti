import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildSession } from '@/test/fixtures';
import { renderRoutes, renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { UserMenu } from './index';

describe('User menu', () => {
  it('shows the initials in a 28 px avatar with an explicit font size, plus the name, and is named for the owner', async () => {
    server.use(
      http.get('/api/auth/session', () =>
        HttpResponse.json(buildSession({ user: { email: 'hem@invalid.test', displayName: 'Hem Singh' } })),
      ),
    );
    renderWithProviders(<UserMenu />);

    const button = await screen.findByRole('button', { name: 'Account menu, Hem Singh' });
    const initials = within(button).getByText('HS');
    const style = getComputedStyle(initials);
    expect(style.fontSize).toBe('13px');
    expect(style.fontWeight).toBe('700');
    expect(style.width).toBe('28px');
    // Not in MUI's startIcon slot, which forces a 20 px font on its content.
    expect(initials.closest('.MuiButton-startIcon')).toBeNull();
    expect(within(button).getByText('Hem Singh')).toBeInTheDocument();
    expect(within(button).getByTestId('ExpandMoreIcon')).toBeInTheDocument();
  });

  it('opens the menu with the name, email and Sign out', async () => {
    renderWithProviders(<UserMenu />);
    await userEvent.click(await screen.findByRole('button', { name: /^Account menu/ }));
    const menu = await screen.findByRole('menu', { name: 'Account' });
    expect(within(menu).getByText('example@invalid.test')).toBeInTheDocument();
    expect(within(menu).getByRole('menuitem', { name: 'Sign out' })).toBeInTheDocument();
  });

  it('lists My account above Sign out and opens the account page from it', async () => {
    const { router } = renderRoutes([
      { path: '/', element: <UserMenu /> },
      { path: '/account', element: <h1>Account page</h1> },
    ]);
    await userEvent.click(await screen.findByRole('button', { name: /^Account menu/ }));
    const menu = await screen.findByRole('menu', { name: 'Account' });
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent),
    ).toEqual(['My account', 'Sign out']);

    await userEvent.click(within(menu).getByRole('menuitem', { name: 'My account' }));

    expect(await screen.findByRole('heading', { name: 'Account page' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/account');
  });

  it('is operable with the keyboard alone', async () => {
    const { router } = renderRoutes([
      { path: '/', element: <UserMenu /> },
      { path: '/account', element: <h1>Account page</h1> },
    ]);
    await screen.findByRole('button', { name: /^Account menu/ });
    await userEvent.tab();
    await userEvent.keyboard('{Enter}');
    const first = await screen.findByRole('menuitem', { name: 'My account' });
    await waitFor(() => {
      expect(first).toHaveFocus();
    });
    await userEvent.keyboard('{Enter}');

    expect(router.state.location.pathname).toBe('/account');
  });
});
