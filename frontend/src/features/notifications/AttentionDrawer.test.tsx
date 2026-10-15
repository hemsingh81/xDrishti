import { screen, waitFor, within, type BoundFunctions, type queries } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import {
  buildDividendsNotice,
  buildReconnectNotice,
  buildReviewNotice,
  buildSyncFailingNotice,
  buildUnmatchedNotice,
  resolved,
} from '@/test/attentionFixtures';
import {
  buildAccountDetail,
  buildConnected,
  buildConnection,
  buildSwingAccount,
  ids,
  summaryOf,
} from '@/test/fixtures';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { findToast } from '@/test/toast';
import { NotificationBell } from './components/NotificationBell';
import { readStorageKey, type ReadStorage } from './lib/readState';

class FakeStorage implements ReadStorage {
  data = new Map<string, string>();
  getItem = (key: string) => this.data.get(key) ?? null;
  setItem = (key: string, value: string) => {
    this.data.set(key, value);
  };
}

type Notice = ReturnType<typeof buildReconnectNotice>;

/** Serves the Portfolio notices (and a quiet account list) and counts how often the notices are read. */
function serve(items: Notice[]) {
  const reads = { notices: 0 };
  server.use(
    http.get('/api/accounts', () =>
      HttpResponse.json([summaryOf(buildAccountDetail({ connection: buildConnected() }))]),
    ),
    http.get('/api/portfolio/notices', () => {
      reads.notices += 1;
      return HttpResponse.json({ items });
    }),
  );
  return reads;
}

function renderBell(storage: ReadStorage | null = new FakeStorage()) {
  return renderRoutes([
    { path: '/', element: <NotificationBell storage={storage} /> },
    { path: '/accounts', element: <p>Accounts page</p> },
    { path: '/portfolio', element: <p>Portfolio page</p> },
  ]);
}

const openDrawer = async (name: string | RegExp) => {
  await userEvent.click(await screen.findByRole('button', { name }));
  return within(await screen.findByRole('dialog', { name: 'Notifications' }));
};
const itemsOf = (drawer: BoundFunctions<typeof queries>) =>
  within(drawer.getByRole('list', { name: 'Notifications' })).getAllByRole('listitem');

// The fixtures carry fixed timestamps from 14 Oct 2026, so the relative times ("… ago") depend on the clock: pin it.
pinClock('2026-10-14T13:00:00Z');

describe('Notification drawer: items that need the owner', () => {
  it('lists the open items most urgent first with severity, title, text, time and buttons', async () => {
    serve([buildDividendsNotice(), buildReviewNotice(), buildReconnectNotice()]);
    renderBell();

    const drawer = await openDrawer('Notifications, 3 unread');
    const items = itemsOf(drawer);

    expect(items).toHaveLength(3);
    expect(items[0]).toHaveTextContent('Error: unread, Reconnect Dhan');
    expect(items[0]).toHaveTextContent('The Dhan login of Test Dhan Main has expired.');
    expect(within(items[0]!).getByText(/ago$/)).toBeInTheDocument();
    expect(within(items[0]!).getByRole('button', { name: 'Reconnect Dhan' })).toBeInTheDocument();
    expect(items[1]).toHaveTextContent('Warning: unread, Holdings need your review');
    expect(within(items[1]!).getByRole('button', { name: 'Review' })).toBeInTheDocument();
    expect(items[2]).toHaveTextContent('Information: unread, Possible dividends in the ledger');
    // Items with buttons have no separate Open link.
    expect(drawer.queryAllByRole('link')).toHaveLength(0);
  });

  it('orders items of one severity newest first and shows at most the two buttons the API sent', async () => {
    const older = buildUnmatchedNotice({ at: '2026-10-13T10:00:00Z' });
    const newer = buildReviewNotice({
      actions: [
        { label: 'Review', kind: 'route', target: '/portfolio' },
        { label: 'Open', kind: 'route', target: '/portfolio' },
        { label: 'Third', kind: 'route', target: '/portfolio' },
      ],
    });
    serve([older, newer]);
    renderBell();

    const items = itemsOf(await openDrawer('Notifications, 2 unread'));

    expect(items[0]).toHaveTextContent('Holdings need your review');
    expect(items[1]).toHaveTextContent('Securities with no match in the instrument list');
    expect(
      within(items[0]!)
        .getAllByRole('button')
        .map((b) => b.textContent),
    ).toEqual(['Review', 'Open']);
  });

  it('keeps what is resolved in a collapsed group that does not count as unread', async () => {
    serve([buildReconnectNotice(), resolved(buildSyncFailingNotice())]);
    renderBell();

    const drawer = await openDrawer('Notifications, 1 unread');

    expect(itemsOf(drawer)).toHaveLength(1);
    expect(drawer.getByRole('heading', { name: 'Unresolved' })).toBeInTheDocument();
    const toggle = drawer.getByRole('button', { name: 'Resolved (1)' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(drawer.queryByRole('list', { name: 'Resolved notifications' })).not.toBeInTheDocument();
    expect(drawer.queryByText('Trades are not being read')).not.toBeInTheDocument();

    await userEvent.click(toggle);

    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    const done = within(await drawer.findByRole('list', { name: 'Resolved notifications' })).getByRole('listitem');
    expect(done).toHaveTextContent('Resolved: Trades are not being read');
    expect(within(done).getByText(/^Resolved · /)).toBeInTheDocument();
    // Nothing to do about it any more: no buttons.
    expect(within(done).queryByRole('button')).not.toBeInTheDocument();

    await userEvent.click(toggle);
    await waitFor(() => {
      expect(drawer.queryByRole('list', { name: 'Resolved notifications' })).not.toBeInTheDocument();
    });
  });

  it('says it is all caught up, and still offers the resolved ones, when only those are left', async () => {
    serve([resolved(buildReconnectNotice())]);
    renderBell();

    const drawer = await openDrawer('Notifications');

    expect(await drawer.findByText("You're all caught up")).toBeInTheDocument();
    expect(drawer.getByRole('button', { name: 'Resolved (1)' })).toBeInTheDocument();
    expect(drawer.getByRole('button', { name: 'Mark all read' })).toBeDisabled();
  });

  it('marks every item read with "Mark all read", resolved ones included, and keeps that for the next visit', async () => {
    const storage = new FakeStorage();
    const notices = [buildReconnectNotice(), buildReviewNotice(), resolved(buildSyncFailingNotice())];
    serve(notices);
    const first = renderBell(storage);
    const drawer = await openDrawer('Notifications, 2 unread');

    await userEvent.click(drawer.getByRole('button', { name: 'Mark all read' }));

    expect(await screen.findByRole('button', { name: 'Notifications', hidden: true })).toBeInTheDocument();
    const stored = JSON.parse(storage.data.get(readStorageKey) ?? '[]') as string[];
    expect(stored.sort()).toEqual(notices.map((n) => `${n.id}|${n.since ?? ''}`).sort());
    first.unmount();

    renderBell(storage);
    expect(await screen.findByRole('button', { name: 'Notifications' })).toBeInTheDocument();
  });

  it('shows a changed situation (a new id) as unread again', async () => {
    const storage = new FakeStorage();
    serve([buildSyncFailingNotice()]);
    const first = renderBell(storage);
    const drawer = await openDrawer('Notifications, 1 unread');
    await userEvent.click(drawer.getByRole('button', { name: 'Mark all read' }));
    first.unmount();

    serve([buildSyncFailingNotice({ id: `attention:sync:${ids.main}:trades:rate_limited` })]);
    renderBell(storage);
    expect(await screen.findByRole('button', { name: 'Notifications, 1 unread' })).toBeInTheDocument();
  });

  it('treats the older "warn" like "warning"', async () => {
    serve([buildReviewNotice({ severity: 'warn' })]);
    renderBell();

    const items = itemsOf(await openDrawer('Notifications, 1 unread'));
    expect(items[0]).toHaveTextContent('Warning: unread,');
  });
});

describe('Notification drawer: the Dhan login', () => {
  it('shows it once, from the server item; the app no longer derives not connected, expired or expiring itself', async () => {
    const hour = 3_600_000;
    server.use(
      http.get('/api/accounts', () =>
        HttpResponse.json(
          [
            buildSwingAccount(),
            buildAccountDetail({
              connection: buildConnection({ status: 'expired', expiresAt: new Date(Date.now() - hour).toISOString() }),
            }),
            buildAccountDetail({
              id: '00000000-0000-4000-8000-000000000009',
              name: 'Test Dhan Third',
              connection: buildConnected({ expiresAt: new Date(Date.now() + hour).toISOString() }),
            }),
          ].map(summaryOf),
        ),
      ),
      http.get('/api/portfolio/notices', () => HttpResponse.json({ items: [buildReconnectNotice()] })),
    );
    renderBell();

    const items = itemsOf(await openDrawer('Notifications, 1 unread'));

    expect(items).toHaveLength(1);
    expect(items[0]).toHaveTextContent('Reconnect Dhan');
    expect(screen.queryByText(/is not connected|token expired|token expires/)).not.toBeInTheDocument();
  });
});

describe('Notification drawer: buttons', () => {
  it('goes to the page of a route button, marks the item read and closes the drawer', async () => {
    const review = buildReviewNotice();
    serve([review, buildReconnectNotice()]);
    const { router } = renderBell();
    const drawer = await openDrawer('Notifications, 2 unread');

    await userEvent.click(within(itemsOf(drawer)[1]!).getByRole('button', { name: 'Review' }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/portfolio');
    });
    expect(router.state.location.search).toContain('&section=trades&rebuild=preview');
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Notifications' })).not.toBeInTheDocument();
    });
    expect(await screen.findByText('Portfolio page')).toBeInTheDocument();
  });

  it('runs a command button with a POST, reads the notices again and tells the result in a toast', async () => {
    const reads = serve([buildSyncFailingNotice()]);
    const calls: string[] = [];
    server.use(
      http.post('/api/accounts/:id/sync/broker-history', ({ params }) => {
        calls.push(String(params.id));
        return HttpResponse.json({ id: 'run' }, { status: 202 });
      }),
    );
    renderBell();
    const drawer = await openDrawer('Notifications, 1 unread');
    await waitFor(() => {
      expect(reads.notices).toBeGreaterThanOrEqual(1);
    });
    const before = reads.notices;

    await userEvent.click(drawer.getByRole('button', { name: 'Retry now' }));

    expect(await findToast()).toHaveTextContent('Retry now: started. This notice goes away by itself once it works.');
    expect(calls).toEqual([ids.main]);
    await waitFor(() => {
      expect(reads.notices).toBeGreaterThan(before);
    });
    // A command does not take the owner away from the list.
    expect(screen.getByRole('dialog', { name: 'Notifications' })).toBeInTheDocument();
  });

  it("tells in a toast, in the API's words, when the command is refused", async () => {
    serve([buildSyncFailingNotice()]);
    server.use(
      http.post('/api/accounts/:id/sync/broker-history', () =>
        problemResponse(
          { title: 'Conflict', detail: 'A sync of this account is already running.', code: 'portfolio.sync_running' },
          409,
        ),
      ),
    );
    renderBell();
    const drawer = await openDrawer('Notifications, 1 unread');

    await userEvent.click(drawer.getByRole('button', { name: 'Retry now' }));

    expect(await findToast()).toHaveTextContent('Retry now did not work. A sync of this account is already running.');
    expect(drawer.getByRole('button', { name: 'Retry now' })).toBeEnabled();
  });

  it('runs the Retry now of a holdings sync and, when the API holds it back for confirmation, says so in a toast', async () => {
    serve([
      buildSyncFailingNotice({
        id: `attention:sync:${ids.main}:holdings:unavailable`,
        actions: [
          { label: 'Retry now', kind: 'command', target: `POST /api/accounts/${ids.main}/holdings/sync` },
          { label: 'Open', kind: 'route', target: `/accounts?account=${ids.main}&tab=portfolio` },
        ],
      }),
    ]);
    server.use(
      http.post('/api/accounts/:id/holdings/sync', () =>
        problemResponse(
          {
            title: 'Confirm to continue.',
            detail: 'Dhan listed 0 holdings while 5 are open, so all of them would be closed.',
            code: 'portfolio.confirmation_required',
          },
          409,
        ),
      ),
    );
    renderBell();
    const drawer = await openDrawer('Notifications, 1 unread');

    await userEvent.click(drawer.getByRole('button', { name: 'Retry now' }));

    const toast = await findToast();
    expect(toast).toHaveTextContent('Retry now did not work. Dhan listed 0 holdings while 5 are open');
    expect(toast).toHaveTextContent('press Sync now to check and confirm it');
  });

  it('refuses to run a command that is not a METHOD /api path of the app, and to follow a route outside the app', async () => {
    serve([
      buildSyncFailingNotice({
        actions: [
          { label: 'Sneaky', kind: 'command', target: 'POST https://example.org/api/x' },
          { label: 'Away', kind: 'route', target: '//example.org/phish' },
          { label: 'Fine', kind: 'route', target: '/portfolio' },
        ],
      }),
    ]);
    renderBell();

    const items = itemsOf(await openDrawer('Notifications, 1 unread'));
    expect(
      within(items[0]!)
        .getAllByRole('button')
        .map((b) => b.textContent),
    ).toEqual(['Fine']);
  });

  it('can be used with the keyboard alone: Tab reaches the button and Enter presses it', async () => {
    serve([buildReconnectNotice()]);
    const { router } = renderBell();
    await openDrawer('Notifications, 1 unread');
    const button = screen.getByRole('button', { name: 'Reconnect Dhan' });

    for (let presses = 0; presses < 8 && document.activeElement !== button; presses += 1) {
      await userEvent.tab();
    }
    expect(button).toHaveFocus();
    await userEvent.keyboard('{Enter}');

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/accounts');
    });
    expect(router.state.location.search).toBe(`?account=${ids.main}&tab=connection`);
  });

  it('names the group of buttons after the item, so several "Review" buttons can be told apart', async () => {
    serve([buildReviewNotice(), buildDividendsNotice()]);
    renderBell();

    const drawer = await openDrawer('Notifications, 2 unread');

    expect(drawer.getByRole('group', { name: 'Actions: Holdings need your review' })).toBeInTheDocument();
    expect(drawer.getByRole('group', { name: 'Actions: Possible dividends in the ledger' })).toBeInTheDocument();
  });
});
