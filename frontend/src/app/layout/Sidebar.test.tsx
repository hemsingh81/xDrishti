import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { QueryClient } from '@tanstack/react-query';
import { http, HttpResponse } from 'msw';
import type { AccountDetail } from '@/shared/api';
import { buildAccountDetail, buildConnected, buildSwingAccount, summaryOf, withConnection } from '@/test/fixtures';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { navHue } from '@/theme';
import { allNavItems } from '../navigation';
import { Sidebar } from './Sidebar';
import { pendingResponse } from '@/test/gate';

describe('Sidebar', () => {
  it('shows a coloured icon chip on every item, planned ones included', () => {
    renderWithProviders(<Sidebar />);
    for (const item of allNavItems) {
      const link = screen.getByRole('link', { name: new RegExp(item.label) });
      const chip = link.querySelector('[data-hue]');
      expect(chip, item.label).toHaveAttribute('data-hue', navHue[item.path]);
    }
  });

  it('gives every navigation route a hue', () => {
    for (const item of allNavItems) {
      expect(navHue[item.path], item.label).toBeDefined();
    }
  });

  it('keeps the chip hue on the selected item', () => {
    renderWithProviders(<Sidebar />, { path: '/' });
    const link = screen.getByRole('link', { name: 'Today' });
    expect(link).toHaveAttribute('aria-current', 'page');
    expect(link.querySelector('[data-hue]')).toHaveAttribute('data-hue', 'today');
  });

  describe('accounts badge', () => {
    const serveAccounts = (...accounts: AccountDetail[]) => {
      server.use(http.get('/api/accounts', () => HttpResponse.json(accounts.map(summaryOf))));
    };
    const expiredMain = () => withConnection(buildAccountDetail(), buildConnected({ status: 'expired' }));
    const accountsLink = () => screen.findByRole('link', { name: /^Accounts, / });

    it('counts an expired connection, names it for assistive technology and explains it in a tooltip', async () => {
      serveAccounts(expiredMain());
      renderWithProviders(<Sidebar />);

      const link = await screen.findByRole('link', { name: 'Accounts, 1 needs reconnecting' });

      expect(within(link).getByText('1')).toBeInTheDocument();
      await userEvent.hover(link);
      expect(await screen.findByRole('tooltip')).toHaveTextContent(
        'Test Dhan Main — expired, open Accounts to reconnect',
      );
    });

    it('counts an account that is not connected, and lists every account that needs action', async () => {
      serveAccounts(
        expiredMain(),
        buildSwingAccount({ name: 'Test Dhan Swing' }),
        buildAccountDetail({ id: 'x', name: 'Fine' }),
      );
      renderWithProviders(<Sidebar />);

      const link = await screen.findByRole('link', { name: 'Accounts, 2 need reconnecting' });

      await userEvent.hover(link);
      const tooltip = await screen.findByRole('tooltip');
      expect(tooltip).toHaveTextContent('Test Dhan Main — expired, open Accounts to reconnect');
      expect(tooltip).toHaveTextContent('Test Dhan Swing — not connected, open Accounts to connect');
      expect(tooltip).not.toHaveTextContent('Fine');
    });

    const loaded = (client: QueryClient, status: 'success' | 'error') =>
      vi.waitFor(() => {
        expect(client.getQueryState(['accounts', 'list'])?.status).toBe(status);
      });

    it('shows no badge when every account is connected', async () => {
      serveAccounts(buildAccountDetail());
      const { client } = renderWithProviders(<Sidebar />);
      await loaded(client, 'success');

      expect(screen.getByRole('link', { name: 'Accounts' })).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: /reconnecting/ })).not.toBeInTheDocument();
    });

    it('shows no badge while the accounts load', () => {
      server.use(http.get('/api/accounts', () => pendingResponse()));
      renderWithProviders(<Sidebar />);

      expect(screen.getByRole('link', { name: 'Accounts' })).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: /reconnecting/ })).not.toBeInTheDocument();
    });

    it('shows no badge when the accounts cannot be loaded', async () => {
      server.use(http.get('/api/accounts', () => HttpResponse.json({ title: 'Service unavailable' }, { status: 503 })));
      const { client } = renderWithProviders(<Sidebar />);
      await loaded(client, 'error');

      expect(screen.getByRole('link', { name: 'Accounts' })).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: /reconnecting/ })).not.toBeInTheDocument();
    });

    it('flips a connected token to expired when its time passes, without refetching', async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      try {
        let fetches = 0;
        server.use(
          http.get('/api/accounts', () => {
            fetches += 1;
            return HttpResponse.json([
              summaryOf(
                buildAccountDetail({
                  connection: buildConnected({ expiresAt: new Date(Date.now() + 60_000).toISOString() }),
                }),
              ),
            ]);
          }),
        );
        renderWithProviders(<Sidebar />);
        await screen.findByRole('link', { name: 'Accounts' });
        await vi.waitFor(() => {
          expect(fetches).toBe(1);
        });
        expect(screen.queryByRole('link', { name: /reconnecting/ })).not.toBeInTheDocument();

        await act(async () => {
          await vi.advanceTimersByTimeAsync(90_000);
        });

        expect(await accountsLink()).toHaveAccessibleName('Accounts, 1 needs reconnecting');
        expect(fetches).toBe(1);
      } finally {
        vi.useRealTimers();
      }
    });
  });
});
