import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildAccountDetail, buildConnected, buildDataNotice, summaryOf } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { buildServiceNotice } from '@/test/servicesFixtures';
import { NotificationBell } from './components/NotificationBell';
import { deriveServiceNotifications } from './lib/deriveServiceNotifications';

const serve = (items: ReturnType<typeof buildServiceNotice>[], data: ReturnType<typeof buildDataNotice>[] = []) => {
  server.use(
    http.get('/api/accounts', () =>
      HttpResponse.json([summaryOf(buildAccountDetail({ connection: buildConnected() }))]),
    ),
    http.get('/api/services/notices', () => HttpResponse.json({ items })),
    http.get('/api/data/notices', () => HttpResponse.json({ items: data })),
  );
};

const renderBell = () =>
  renderRoutes([
    { path: '/', element: <NotificationBell storage={null} /> },
    { path: '/services', element: <p>Services screen</p> },
  ]);

describe('Notification bell: service notices', () => {
  it('shows a failed service next to the data notices, errors first', async () => {
    serve([buildServiceNotice()], [buildDataNotice()]);
    renderBell();

    await userEvent.click(await screen.findByRole('button', { name: 'Notifications, 2 unread' }));
    const drawer = await screen.findByRole('dialog', { name: 'Notifications' });
    const items = within(within(drawer).getByRole('list', { name: 'Notifications' })).getAllByRole('listitem');

    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent('Error: unread,');
    expect(items[0]).toHaveTextContent('End-of-day data run failed: Dhan refused the access token.');
    expect(items[1]).toHaveTextContent('4 sessions have missing 1-minute bars');
  });

  it('opens the Services screen from the notice', async () => {
    serve([buildServiceNotice()]);
    const { router } = renderBell();

    await userEvent.click(await screen.findByRole('button', { name: 'Notifications, 1 unread' }));
    const drawer = await screen.findByRole('dialog', { name: 'Notifications' });
    const link = within(drawer).getByRole('link', {
      name: 'Open: End-of-day data run failed: Dhan refused the access token.',
    });
    expect(link).toHaveAttribute('href', '/services');
    await userEvent.click(link);

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/services');
    });
    expect(await screen.findByText('Services screen')).toBeInTheDocument();
  });

  it('shows nothing about services when none failed, and keeps the bell usable when the notices cannot be read', async () => {
    server.use(
      http.get('/api/accounts', () =>
        HttpResponse.json([summaryOf(buildAccountDetail({ connection: buildConnected() }))]),
      ),
      http.get('/api/services/notices', () => HttpResponse.json({ title: 'down' }, { status: 500 })),
    );
    renderBell();

    await userEvent.click(await screen.findByRole('button', { name: 'Notifications' }));
    const drawer = await screen.findByRole('dialog', { name: 'Notifications' });
    expect(within(drawer).getByText("You're all caught up")).toBeInTheDocument();
  });
});

describe('deriveServiceNotifications', () => {
  it('maps the API notice: its id (a newer failed run is a new one), severity, text, time and the Services link', () => {
    const [notice] = deriveServiceNotifications([buildServiceNotice()]);

    expect(notice).toEqual({
      id: 'service:failed:00000000-0000-4000-8000-000000000902',
      kind: 'service_failed',
      severity: 'error',
      accountId: null,
      accountName: null,
      text: 'End-of-day data run failed: Dhan refused the access token.',
      href: '/services',
      at: Date.parse('2026-10-12T15:51:00+05:30'),
    });
  });

  it('drops a kind this version does not know, and keeps an unknown time as null', () => {
    const list = deriveServiceNotifications([
      buildServiceNotice({ kind: 'service_something_new' }),
      buildServiceNotice({ id: 'service:failed:x', at: null }),
    ]);

    expect(list).toHaveLength(1);
    expect(list[0]?.at).toBeNull();
  });
});
