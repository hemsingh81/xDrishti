import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildReconnectNotice } from '@/test/attentionFixtures';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { findToast } from '@/test/toast';
import { routes } from '../router';

// One ToastProvider serves every route (it lives in the shell), so a toast raised on a screen that never had a provider
// (System status) shows, and one that has to stay is not lost when the owner moves to another screen.
// The notice is dated 14 Oct 2026 and the shell shows the account's key and login against today: the clock is pinned.
pinClock('2026-10-15T06:30:00Z');

const serveReconnect = () => {
  server.use(http.get('/api/portfolio/notices', () => HttpResponse.json({ items: [buildReconnectNotice()] })));
};
const stacks = () => screen.queryAllByRole('region', { name: 'Notifications' });

describe('App shell: toasts', () => {
  it.each(['/system', '/settings', '/services'])('shows a toast raised on %s', async (path) => {
    serveReconnect();
    renderRoutes(routes, { initialPath: path });

    const toast = await findToast();

    expect(toast).toHaveTextContent('Reconnect Dhan');
    expect(stacks()).toHaveLength(1);
  });

  it('keeps a toast that has to stay when the owner moves to another screen', async () => {
    serveReconnect();
    const { router } = renderRoutes(routes, { initialPath: '/system' });
    await findToast();

    await act(() => router.navigate('/settings'));
    await screen.findByRole('heading', { level: 1, name: 'Settings' });

    expect(within(stacks()[0]!).getByRole('heading', { name: 'Reconnect Dhan' })).toBeInTheDocument();
    expect(stacks()).toHaveLength(1);
  });

  it('takes the owner to the screen of the toast button inside the shell', async () => {
    serveReconnect();
    const { router } = renderRoutes(routes, { initialPath: '/system' });
    const toast = await findToast();

    await userEvent.click(within(toast).getByRole('button', { name: 'Reconnect Dhan' }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/accounts');
    });
    expect(router.state.location.search).toContain('tab=connection');
    await screen.findByRole('heading', { level: 1, name: 'Accounts' });
  });
});
