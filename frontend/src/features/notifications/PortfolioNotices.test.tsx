import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildAlertNotice, buildPortfolioNotice } from '@/test/actionCenterFixtures';
import { backendCommandTargets, buildReconnectNotice } from '@/test/attentionFixtures';
import { buildAccountDetail, buildConnected, buildDataNotice, ids, summaryOf } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { NotificationBell } from './components/NotificationBell';
import { derivePortfolioNotifications } from './lib/derivePortfolioNotifications';

const serve = (items: ReturnType<typeof buildPortfolioNotice>[], data: ReturnType<typeof buildDataNotice>[] = []) => {
  server.use(
    http.get('/api/accounts', () =>
      HttpResponse.json([summaryOf(buildAccountDetail({ connection: buildConnected() }))]),
    ),
    http.get('/api/portfolio/notices', () => HttpResponse.json({ items })),
    http.get('/api/data/notices', () => HttpResponse.json({ items: data })),
  );
};

const renderBell = () =>
  renderRoutes([
    { path: '/', element: <NotificationBell storage={null} /> },
    { path: '/portfolio', element: <p>Portfolio screen</p> },
  ]);

describe('Notification bell: Portfolio notices', () => {
  it('shows a triggered alert as a warning and the daily reminder to decide as information, among the other notices', async () => {
    serve([buildPortfolioNotice(), buildAlertNotice()], [buildDataNotice()]);
    renderBell();

    await userEvent.click(await screen.findByRole('button', { name: 'Notifications, 3 unread' }));
    const drawer = await screen.findByRole('dialog', { name: 'Notifications' });
    const items = within(within(drawer).getByRole('list', { name: 'Notifications' })).getAllByRole('listitem');

    expect(items).toHaveLength(3);
    // Warnings first, the newest first: the data notice, then the alert; the reminder is information.
    expect(items[0]).toHaveTextContent('4 sessions have missing 1-minute bars');
    expect(items[1]).toHaveTextContent('Warning: unread,');
    expect(items[1]).toHaveTextContent('TCS reached ₹3,912.50, at or above your ₹3,900 alert');
    expect(items[2]).toHaveTextContent('Information: unread,');
    expect(items[2]).toHaveTextContent('5 holdings need a decision');
  });

  it('opens the Portfolio screen with the Alerts panel on the Triggered tab from a triggered alert', async () => {
    serve([buildAlertNotice()]);
    const { router } = renderBell();

    await userEvent.click(await screen.findByRole('button', { name: 'Notifications, 1 unread' }));
    const drawer = await screen.findByRole('dialog', { name: 'Notifications' });
    const link = within(drawer).getByRole('link', {
      name: 'Open: TCS reached ₹3,912.50, at or above your ₹3,900 alert',
    });
    expect(link).toHaveAttribute('href', '/portfolio?alerts=triggered');
    await userEvent.click(link);

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/portfolio');
    });
    expect(router.state.location.search).toBe('?alerts=triggered');
    expect(await screen.findByText('Portfolio screen')).toBeInTheDocument();
  });

  it('opens the Portfolio screen at the Action center from the reminder to decide', async () => {
    serve([buildPortfolioNotice()]);
    const { router } = renderBell();

    await userEvent.click(await screen.findByRole('button', { name: 'Notifications, 1 unread' }));
    const drawer = await screen.findByRole('dialog', { name: 'Notifications' });
    await userEvent.click(within(drawer).getByRole('link', { name: 'Open: 5 holdings need a decision' }));

    await waitFor(() => {
      expect(router.state.location.search).toBe('?focus=actions');
    });
  });

  it('keeps the bell usable when the Portfolio notices cannot be read', async () => {
    server.use(
      http.get('/api/accounts', () =>
        HttpResponse.json([summaryOf(buildAccountDetail({ connection: buildConnected() }))]),
      ),
      http.get('/api/portfolio/notices', () => HttpResponse.json({ title: 'down' }, { status: 500 })),
    );
    renderBell();

    await userEvent.click(await screen.findByRole('button', { name: 'Notifications' }));
    const drawer = await screen.findByRole('dialog', { name: 'Notifications' });
    expect(within(drawer).getByText("You're all caught up")).toBeInTheDocument();
  });
});

describe('derivePortfolioNotifications', () => {
  it('maps the API notice: its id (one per alert, one per day), severity, text, time and where Open leads', () => {
    const [alert, decide] = derivePortfolioNotifications([buildAlertNotice(), buildPortfolioNotice()]);

    expect(alert).toEqual({
      id: 'portfolio:alert:00000000-0000-4000-8000-000000001202',
      kind: 'price_alert_triggered',
      severity: 'warn',
      accountId: null,
      accountName: null,
      text: 'TCS reached ₹3,912.50, at or above your ₹3,900 alert',
      href: '/portfolio?alerts=triggered',
      at: Date.parse('2026-10-12T09:40:00Z'),
      title: null,
      actions: [],
      since: null,
      resolved: false,
    });
    expect(decide).toMatchObject({
      id: 'portfolio:actions:2026-10-13',
      kind: 'actions_need_decision',
      severity: 'info',
      href: '/portfolio?focus=actions',
    });
  });

  it('keeps `since` only on the attention items: any other notice is identified by its id alone', () => {
    const since = '2026-10-15T09:00:00Z';
    const [decide, alert, reconnect] = derivePortfolioNotifications([
      buildPortfolioNotice({ since }),
      buildAlertNotice({ since }),
      buildReconnectNotice({ since }),
    ]);

    expect(decide?.since).toBeNull();
    expect(alert?.since).toBeNull();
    expect(reconnect?.since).toBe(since);
  });

  it('keeps every command the API can name: no action is silently dropped', () => {
    const targets = backendCommandTargets(ids.main);
    const [notice] = derivePortfolioNotifications([
      buildReconnectNotice({
        actions: targets.map((target, n) => ({ label: `Run ${n}`, kind: 'command', target })),
      }),
    ]);
    // Only the first two buttons are shown, so each target is tried on its own.
    const kept = targets.map(
      (target) =>
        derivePortfolioNotifications([
          buildReconnectNotice({ actions: [{ label: 'Retry now', kind: 'command', target }] }),
        ])[0]?.actions[0]?.target,
    );

    expect(kept).toEqual(targets);
    expect(notice?.actions).toHaveLength(2);
  });

  it('drops a kind this version does not know and keeps a notice without a time', () => {
    const result = derivePortfolioNotifications([
      buildPortfolioNotice({ id: 'portfolio:future', kind: 'something_new' }),
      buildPortfolioNotice({ at: null }),
    ]);

    expect(result.map((n) => n.id)).toEqual(['portfolio:actions:2026-10-13']);
    expect(result[0]?.at).toBeNull();
  });
});
