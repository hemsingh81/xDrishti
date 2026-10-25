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
import { readStorageKey, type ReadStorage } from './lib/readState';

class FakeStorage implements ReadStorage {
  data = new Map<string, string>();
  getItem = (key: string) => this.data.get(key) ?? null;
  setItem = (key: string, value: string) => {
    this.data.set(key, value);
  };
}

const nightlyFailed = () =>
  buildPlanNotice({
    id: 'plan:nightly:2026-10-26:1',
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

const renderBell = (storage: ReadStorage | null = null) =>
  renderRoutes([
    { path: '/', element: <NotificationBell storage={storage} /> },
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

  it('shows carried ticket notices in the real bell: the open crossing counts, the resolved prompt does not', async () => {
    serve([
      buildPlanNotice({
        id: 'crossing:41',
        kind: 'ticket_level_crossed',
        severity: 'warning',
        text: '1 watched ticket crossed its indicative stop level. Alert only: no order is placed and none exists.',
        since: '2026-10-26T02:00:00Z',
      }),
      buildPlanNotice({
        id: 'prompt:9',
        kind: 'post_trade_prompt',
        severity: 'info',
        text: '1 armed ticket has a trade on record.',
        resolved: true,
        since: '2026-10-26T02:00:00Z',
      }),
    ]);
    renderBell();

    await userEvent.click(await screen.findByRole('button', { name: 'Notifications, 1 unread' }));
    const drawer = await screen.findByRole('dialog', { name: 'Notifications' });

    expect(within(drawer).getByText(/crossed its indicative stop level/)).toBeInTheDocument();
    expect(
      within(drawer)
        .getAllByRole('link')
        .every((l) => l.getAttribute('href') === '/plans?plan=7'),
    ).toBe(true);
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

  it('keeps what was read in the browser when Open is followed, under the id of that situation', async () => {
    const storage = new FakeStorage();
    serve([buildPlanNotice()]);
    renderBell(storage);

    await userEvent.click(await screen.findByRole('button', { name: 'Notifications, 1 unread' }));
    const drawer = await screen.findByRole('dialog', { name: 'Notifications' });
    await userEvent.click(within(drawer).getByRole('link', { name: /^Open: The plan for 26 Oct awaits your review/ }));

    expect(await screen.findByText('Plans screen')).toBeInTheDocument();
    expect(JSON.parse(storage.data.get(readStorageKey) ?? '[]')).toEqual(['plan:review:7:info']);
  });

  it('marks a plan item read with "Mark all read", and the same plan turning urgent is unread again', async () => {
    fakeTimers({ shouldAdvanceTime: true });
    try {
      let reads = 0;
      server.use(
        http.get('/api/accounts', () =>
          HttpResponse.json([summaryOf(buildAccountDetail({ connection: buildConnected() }))]),
        ),
        http.get('/api/plans/notices', () => {
          reads += 1;
          return HttpResponse.json({
            items: [
              reads > 1
                ? buildPlanNotice({
                    id: 'plan:review:7:warning',
                    severity: 'warning',
                    text: 'The plan awaits your review: 30 minutes to the cutoff.',
                  })
                : buildPlanNotice(),
            ],
          });
        }),
      );
      renderBell();
      await userEvent.click(await screen.findByRole('button', { name: 'Notifications, 1 unread' }));
      const drawer = await screen.findByRole('dialog', { name: 'Notifications' });
      await userEvent.click(within(drawer).getByRole('button', { name: 'Mark all read' }));
      expect(within(drawer).getByRole('button', { name: 'Mark all read' })).toBeDisabled();

      await vi.advanceTimersByTimeAsync(planNoticesPollMs + 1_000);

      // The same plan turned urgent under a new id: unread again, so "Mark all read" has something to do.
      await waitFor(() => {
        expect(within(drawer).getByRole('button', { name: 'Mark all read' })).toBeEnabled();
      });
      expect(within(drawer).getByRole('listitem')).toHaveTextContent('Warning: unread,');
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
      id: 'plan:review:7:info',
      kind: 'plan_awaiting_review',
      severity: 'warn',
      accountId: null,
      accountName: null,
      text: 'The plan for 26 Oct awaits your review: 3 tickets undecided, 40 minutes to the cutoff at 09:10. Unreviewed tickets lapse.',
      href: '/plans?plan=7',
      at: Date.parse('2026-10-26T03:40:00Z'),
      title: null,
      actions: [],
      since: null,
      resolved: false,
    });
  });

  it('drops a kind this version does not know and a route that leaves the app, and keeps an unknown time as null', () => {
    const list = derivePlanNotifications([
      buildPlanNotice({ kind: 'plan_something_new' }),
      buildPlanNotice({ id: 'plan:review:8', route: 'https://example.invalid/plans' }),
      buildPlanNotice({ id: 'plan:review:9', route: '//example.invalid/plans' }),
      buildPlanNotice({ id: 'plan:review:10', route: 'javascript:alert(1)' }),
      buildPlanNotice({ id: 'plan:review:11', route: '/\\evil.example' }),
      buildPlanNotice({ id: 'plan:review:12', route: '/portfolio' }),
      buildPlanNotice({ id: 'plan:review:13', route: '/plans?plan=7&x=1' }),
      buildPlanNotice({ id: 'plan:review:14', route: '/plans?plan=abc' }),
      buildPlanNotice({ id: 'plan:review:15', at: 'not a date' }),
      buildPlanNotice({ id: 'plan:armed:7', kind: 'plan_tickets_armed', at: null }),
    ]);

    expect(list.map((n) => n.id)).toEqual(['plan:review:15', 'plan:armed:7']);
    expect(list.every((n) => n.at === null)).toBe(true);
  });

  it('carries the ticket crossing and post-trade prompt kinds as route-only items without actions', () => {
    const list = derivePlanNotifications([
      buildPlanNotice({
        id: 'plan:crossed:7:1',
        kind: 'ticket_level_crossed',
        severity: 'warning',
        route: '/plans?plan=7',
      }),
      buildPlanNotice({ id: 'plan:prompt:7:1', kind: 'post_trade_prompt', severity: 'info', route: '/plans?plan=7' }),
    ]);

    expect(list.map((n) => n.kind)).toEqual(['ticket_level_crossed', 'post_trade_prompt']);
    expect(list.every((n) => n.actions.length === 0 && n.href === '/plans?plan=7')).toBe(true);
  });
});
