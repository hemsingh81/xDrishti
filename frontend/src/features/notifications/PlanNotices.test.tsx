import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { planNoticesPollMs } from '@/shared/api';
import { buildPortfolioNotice } from '@/test/actionCenterFixtures';
import { buildAccountDetail, buildConnected, summaryOf } from '@/test/fixtures';
import { fakeTimers, pinClock } from '@/test/pinClock';
import { buildPlanNotice } from '@/test/planFixtures';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { NotificationBell } from './components/NotificationBell';
import { derivePlanNotifications } from './lib/derivePlanNotifications';

const nightlyFailed = () =>
  buildPlanNotice({
    id: 'plan:nightly:2026-10-26',
    kind: 'plan_nightly_failed',
    severity: 'error',
    text: 'The nightly plan for 26 Oct failed closed at the eod gate: the EOD run has not finished. No plan was published.',
    route: '/plans',
    planId: null,
    at: '2026-10-25T13:05:00Z',
  });

const serve = (
  items: ReturnType<typeof buildPlanNotice>[],
  portfolio: ReturnType<typeof buildPortfolioNotice>[] = [],
) => {
  server.use(
    http.get('/api/accounts', () =>
      HttpResponse.json([summaryOf(buildAccountDetail({ connection: buildConnected() }))]),
    ),
    http.get('/api/plans/notices', () => HttpResponse.json({ items })),
    http.get('/api/portfolio/notices', () => HttpResponse.json({ items: portfolio })),
  );
};

const renderBell = () =>
  renderRoutes([
    { path: '/', element: <NotificationBell storage={null} /> },
    { path: '/plans', element: <p>Plans screen</p> },
  ]);

describe('Notification bell: plan notices', () => {
  pinClock('2026-10-26T03:00:00Z'); // forty minutes before the cutoff of the fixture

  it('shows the failed nightly run first, then the plan to review, as items with Open and no button', async () => {
    serve([buildPlanNotice(), nightlyFailed()]);
    renderBell();

    await userEvent.click(await screen.findByRole('button', { name: 'Notifications, 2 unread' }));
    const drawer = await screen.findByRole('dialog', { name: 'Notifications' });
    const items = within(within(drawer).getByRole('list', { name: 'Notifications' })).getAllByRole('listitem');

    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent('Error: unread,');
    expect(items[0]).toHaveTextContent('failed closed at the eod gate');
    expect(items[1]).toHaveTextContent('Information: unread,');
    expect(items[1]).toHaveTextContent('3 tickets undecided, 40 minutes to the cutoff');
    for (const item of items) {
      expect(within(item).queryByRole('button')).not.toBeInTheDocument();
      expect(within(item).getByRole('link')).toBeInTheDocument();
    }
  });

  it('opens the Plans screen on the plan from the notice', async () => {
    serve([buildPlanNotice()]);
    const { router } = renderBell();

    await userEvent.click(await screen.findByRole('button', { name: 'Notifications, 1 unread' }));
    const drawer = await screen.findByRole('dialog', { name: 'Notifications' });
    const link = within(drawer).getByRole('link', { name: /^Open: The plan for 26 Oct awaits your review/ });
    expect(link).toHaveAttribute('href', '/plans?plan=7');
    await userEvent.click(link);

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/plans');
    });
    expect(router.state.location.search).toBe('?plan=7');
    expect(await screen.findByText('Plans screen')).toBeInTheDocument();
  });

  it('counts a plan item in the badge and ignores a resolved item of another source', async () => {
    serve([buildPlanNotice()], [buildPortfolioNotice({ resolved: true })]);
    renderBell();

    expect(await screen.findByRole('button', { name: 'Notifications, 1 unread' })).toBeInTheDocument();
  });

  it('has no badge and says all caught up when there are no plan items', async () => {
    serve([]);
    renderBell();

    await userEvent.click(await screen.findByRole('button', { name: 'Notifications' }));
    const drawer = await screen.findByRole('dialog', { name: 'Notifications' });
    expect(within(drawer).getByText("You're all caught up")).toBeInTheDocument();
  });

  it('drops the item by itself when the next poll no longer returns it, and the badge goes', async () => {
    fakeTimers({ shouldAdvanceTime: true });
    try {
      let reads = 0;
      server.use(
        http.get('/api/accounts', () =>
          HttpResponse.json([summaryOf(buildAccountDetail({ connection: buildConnected() }))]),
        ),
        http.get('/api/plans/notices', () => {
          reads += 1;
          return HttpResponse.json({ items: reads > 1 ? [] : [buildPlanNotice()] });
        }),
      );
      renderBell();
      expect(await screen.findByRole('button', { name: 'Notifications, 1 unread' })).toBeInTheDocument();

      await vi.advanceTimersByTimeAsync(planNoticesPollMs + 1_000);

      expect(await screen.findByRole('button', { name: 'Notifications' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /unread/ })).not.toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it('keeps the bell usable when the plan notices cannot be read', async () => {
    serve([]);
    server.use(http.get('/api/plans/notices', () => HttpResponse.json({ title: 'down' }, { status: 500 })));
    renderBell();

    await userEvent.click(await screen.findByRole('button', { name: 'Notifications' }));
    const drawer = await screen.findByRole('dialog', { name: 'Notifications' });
    expect(within(drawer).getByText("You're all caught up")).toBeInTheDocument();
  });
});

describe('derivePlanNotifications', () => {
  it('maps the API notice to a bell item with its route, no actions and no resolved flag', () => {
    const [notice] = derivePlanNotifications([buildPlanNotice({ severity: 'warning' })]);

    expect(notice).toEqual({
      id: 'plan:review:7',
      kind: 'plan_awaiting_review',
      severity: 'warn',
      accountId: null,
      accountName: null,
      text: 'The plan for 26 Oct awaits your review: 3 tickets undecided, 40 minutes to the cutoff at 09:10. Unreviewed tickets lapse.',
      href: '/plans?plan=7',
      at: Date.parse('2026-10-26T03:40:00Z'),
      title: null,
      actions: [],
      resolved: false,
    });
  });

  it('drops a kind this version does not know and a route that leaves the app, and keeps an unknown time as null', () => {
    const list = derivePlanNotifications([
      buildPlanNotice({ kind: 'plan_something_new' }),
      buildPlanNotice({ id: 'plan:review:8', route: 'https://example.invalid/plans' }),
      buildPlanNotice({ id: 'plan:review:9', route: '//example.invalid/plans' }),
      buildPlanNotice({ id: 'plan:armed:7', kind: 'plan_tickets_armed', at: null }),
    ]);

    expect(list).toHaveLength(1);
    expect(list[0]?.at).toBeNull();
  });
});
