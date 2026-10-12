import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { Suspense } from 'react';
import { dataNoticesPollMs, type AccountDetail } from '@/shared/api';
import { buildActionsNotice, buildQualityNotice } from '@/test/actionFixtures';
import {
  buildAccountDetail,
  buildConnected,
  buildDataNotice,
  buildRunFailedNotice,
  buildSwingAccount,
  summaryOf,
} from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { NotificationBell } from './components/NotificationBell';
import { NotificationBell as LazyBell } from './index';
import { readStorageKey, type ReadStorage } from './lib/readState';

class FakeStorage implements ReadStorage {
  data = new Map<string, string>();
  getItem = (key: string) => this.data.get(key) ?? null;
  setItem = (key: string, value: string) => {
    this.data.set(key, value);
  };
}

const serve = (...accounts: AccountDetail[]) => {
  server.use(http.get('/api/accounts', () => HttpResponse.json(accounts.map(summaryOf))));
};
const serveNotices = (...items: ReturnType<typeof buildDataNotice>[]) => {
  server.use(http.get('/api/data/notices', () => HttpResponse.json({ items })));
};
const day = 86_400_000;
const keyExpiring = (days: number) => new Date(Date.now() + days * day).toISOString().slice(0, 10);

/** Main is fine; Swing is not connected and its key is about to expire: two notifications. */
const twoProblems = () => [buildAccountDetail(), buildSwingAccount({ apiKeyExpiresOn: keyExpiring(10) })];

function renderBell(storage: ReadStorage | null = new FakeStorage()) {
  return renderRoutes([
    { path: '/', element: <NotificationBell storage={storage} /> },
    { path: '/accounts', element: <p>Accounts page</p> },
  ]);
}

/** The bell is aria-hidden while the drawer is open (modal), so look it up including hidden content. */
const bellNamed = (name: string) => screen.findByRole('button', { name, hidden: true });

const openDrawer = async (name: string | RegExp) => {
  await userEvent.click(await screen.findByRole('button', { name }));
  return screen.findByRole('dialog', { name: 'Notifications' });
};

describe('Notification bell', () => {
  it('shows the unread count in its name and badge', async () => {
    serve(...twoProblems());
    renderBell();
    expect(await screen.findByRole('button', { name: 'Notifications, 2 unread' })).toHaveTextContent('2');
  });

  it('lists the notifications with text, relative time and an Open link', async () => {
    serve(...twoProblems());
    renderBell();
    const drawer = await openDrawer('Notifications, 2 unread');
    const items = within(within(drawer).getByRole('list', { name: 'Notifications' })).getAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(within(items[0]!).getByText('Test Dhan Swing is not connected')).toBeInTheDocument();
    expect(within(items[1]!).getByText(/API key expires on/)).toBeInTheDocument();
    expect(within(items[1]!).getByText(/^in \d+ days$/)).toBeInTheDocument();
    expect(within(items[0]!).getByRole('link', { name: 'Open: Test Dhan Swing is not connected' })).toHaveAttribute(
      'href',
      expect.stringMatching(/^\/accounts\?account=.+&tab=connection$/),
    );
  });

  it('marks everything read with "Mark all read"; opening the drawer alone does not', async () => {
    serve(...twoProblems());
    renderBell();
    const drawer = await openDrawer('Notifications, 2 unread');
    expect(await bellNamed('Notifications, 2 unread')).toBeInTheDocument();

    await userEvent.click(within(drawer).getByRole('button', { name: 'Mark all read' }));

    expect(await bellNamed('Notifications')).toBeInTheDocument();
    expect(within(drawer).getByRole('button', { name: 'Mark all read' })).toBeDisabled();
    expect(within(drawer).getAllByRole('listitem')).toHaveLength(2);
  });

  it('marks one read and navigates when Open is followed', async () => {
    serve(...twoProblems());
    const { router } = renderBell();
    const drawer = await openDrawer('Notifications, 2 unread');

    await userEvent.click(within(drawer).getByRole('link', { name: 'Open: Test Dhan Swing is not connected' }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/accounts');
    });
    expect(router.state.location.search).toContain('tab=connection');
    expect(await screen.findByText('Accounts page')).toBeInTheDocument();
  });

  it('keeps the read state in storage across a reload, and shows a changed expiry as new', async () => {
    const storage = new FakeStorage();
    serve(...twoProblems());
    const first = renderBell(storage);
    const drawer = await openDrawer('Notifications, 2 unread');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Mark all read' }));
    expect(storage.data.has(readStorageKey)).toBe(true);
    first.unmount();

    // Reload: nothing unread.
    const second = renderBell(storage);
    expect(await screen.findByRole('button', { name: 'Notifications' })).toBeInTheDocument();
    second.unmount();

    // The key's expiry moved: that is a different situation, so it is unread again.
    serve(buildAccountDetail(), buildSwingAccount({ apiKeyExpiresOn: keyExpiring(12) }));
    renderBell(storage);
    expect(await screen.findByRole('button', { name: 'Notifications, 1 unread' })).toBeInTheDocument();
  });

  it('works without any storage', async () => {
    serve(...twoProblems());
    renderBell(null);
    const drawer = await openDrawer('Notifications, 2 unread');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Mark all read' }));
    expect(await bellNamed('Notifications')).toBeInTheDocument();
  });

  it('caps the badge at 9+', async () => {
    const many = Array.from({ length: 11 }, (_, i) =>
      buildSwingAccount({ id: `00000000-0000-4000-8000-00000000${String(100 + i)}`, name: `Account ${String(i)}` }),
    );
    serve(...many);
    renderBell();
    expect(await screen.findByRole('button', { name: 'Notifications, 11 unread' })).toHaveTextContent('9+');
  });

  it("says you're all caught up when there is nothing to report", async () => {
    serve(buildAccountDetail({ connection: buildConnected() }));
    renderBell();
    const drawer = await openDrawer('Notifications');
    expect(within(drawer).getByText("You're all caught up")).toBeInTheDocument();
    expect(within(drawer).getByRole('button', { name: 'Mark all read' })).toBeDisabled();
  });

  it('stays usable when the accounts cannot be loaded, and can try again', async () => {
    server.use(http.get('/api/accounts', () => HttpResponse.json({ title: 'down' }, { status: 500 })));
    renderBell();
    const drawer = await openDrawer('Notifications');
    expect(await within(drawer).findByText('Could not load the notifications.')).toBeInTheDocument();
    serve(...twoProblems());
    await userEvent.click(within(drawer).getByRole('button', { name: 'Try again' }));
    expect(await bellNamed('Notifications, 2 unread')).toBeInTheDocument();
  });

  it('closes with the close button and gives focus back to the bell', async () => {
    serve(...twoProblems());
    renderBell();
    const drawer = await openDrawer('Notifications, 2 unread');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Close notifications' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Notifications' })).not.toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: 'Notifications, 2 unread' })).toHaveFocus();
  });

  it('is available through the feature index (lazy)', async () => {
    serve(...twoProblems());
    renderRoutes([
      {
        path: '/',
        element: (
          <Suspense fallback={null}>
            <LazyBell storage={new FakeStorage()} />
          </Suspense>
        ),
      },
    ]);
    expect(await screen.findByRole('button', { name: 'Notifications, 2 unread' })).toBeInTheDocument();
  });
});

describe('Notification bell: data notices', () => {
  it('shows the data notices with the account ones, errors first, newest first within a level', async () => {
    serve(...twoProblems());
    serveNotices(buildDataNotice(), buildRunFailedNotice());
    renderBell();

    const drawer = await openDrawer('Notifications, 4 unread');
    const items = within(within(drawer).getByRole('list', { name: 'Notifications' })).getAllByRole('listitem');
    expect(items).toHaveLength(4);
    // Error first (the failed run), then the warnings by time: the gaps (12 Oct) before the account (never connected).
    expect(items[0]).toHaveTextContent('Error: unread,');
    expect(items[0]).toHaveTextContent('The automatic update of 12 Oct failed: Dhan refused the access token.');
    expect(items[1]).toHaveTextContent('Warning: unread,');
    expect(items[1]).toHaveTextContent('4 sessions have missing 1-minute bars');
    expect(items[2]).toHaveTextContent('Test Dhan Swing is not connected');
  });

  it('links the failed run to the Data screen and the gaps to their list', async () => {
    serve(buildAccountDetail());
    serveNotices(buildDataNotice(), buildRunFailedNotice());
    renderBell();

    const drawer = await openDrawer('Notifications, 2 unread');

    expect(
      within(drawer).getByRole('link', {
        name: 'Open: The automatic update of 12 Oct failed: Dhan refused the access token.',
      }),
    ).toHaveAttribute('href', '/data');
    expect(within(drawer).getByRole('link', { name: 'Open: 4 sessions have missing 1-minute bars' })).toHaveAttribute(
      'href',
      '/data?gaps=open',
    );
  });

  it('shows the failed quality checks and the suggested corporate actions, with where Open leads', async () => {
    serve(buildAccountDetail());
    serveNotices(buildActionsNotice(), buildQualityNotice());
    renderBell();

    const drawer = await openDrawer('Notifications, 2 unread');

    const items = within(within(drawer).getByRole('list', { name: 'Notifications' })).getAllByRole('listitem');
    expect(items[0]).toHaveTextContent('Error: unread,');
    expect(items[0]).toHaveTextContent('3 instruments fail the data quality checks; the nightly plan is blocked');
    expect(items[1]).toHaveTextContent('Warning: unread,');
    expect(items[1]).toHaveTextContent('2 suggested corporate actions wait for your decision');
    expect(
      within(drawer).getByRole('link', {
        name: 'Open: 3 instruments fail the data quality checks; the nightly plan is blocked',
      }),
    ).toHaveAttribute('href', '/quality');
    expect(
      within(drawer).getByRole('link', { name: 'Open: 2 suggested corporate actions wait for your decision' }),
    ).toHaveAttribute('href', '/data?section=actions&actions=suggested');
  });

  it('marks a data notice read, and shows a changed situation (a new id) as unread again', async () => {
    const storage = new FakeStorage();
    serve(buildAccountDetail());
    serveNotices(buildDataNotice());
    const first = renderBell(storage);
    const drawer = await openDrawer('Notifications, 1 unread');
    await userEvent.click(within(drawer).getByRole('button', { name: 'Mark all read' }));
    expect(await bellNamed('Notifications')).toBeInTheDocument();
    first.unmount();

    serveNotices(buildDataNotice());
    const same = renderBell(storage);
    expect(await screen.findByRole('button', { name: 'Notifications' })).toBeInTheDocument();
    same.unmount();

    serveNotices(buildDataNotice({ id: 'data:gaps_open:1760266320:7', text: '7 sessions have missing 1-minute bars' }));
    renderBell(storage);
    expect(await screen.findByRole('button', { name: 'Notifications, 1 unread' })).toBeInTheDocument();
  });

  it('adds nothing when there are no data notices', async () => {
    serve(buildAccountDetail({ connection: buildConnected() }));
    renderBell();

    const drawer = await openDrawer('Notifications');
    expect(within(drawer).getByText("You're all caught up")).toBeInTheDocument();
  });

  it('keeps the account notifications when the data notices cannot be read', async () => {
    serve(...twoProblems());
    server.use(http.get('/api/data/notices', () => HttpResponse.json({ title: 'down' }, { status: 500 })));
    renderBell();

    const drawer = await openDrawer('Notifications, 2 unread');
    expect(within(drawer).getAllByRole('listitem')).toHaveLength(2);
    expect(within(drawer).queryByText('Could not load the notifications.')).not.toBeInTheDocument();
  });

  it('asks for the data notices again when the poll interval passes', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      let reads = 0;
      server.use(
        http.get('/api/data/notices', () => {
          reads += 1;
          return HttpResponse.json({ items: reads > 1 ? [buildRunFailedNotice()] : [] });
        }),
      );
      serve(buildAccountDetail({ connection: buildConnected() }));
      renderBell();
      expect(await screen.findByRole('button', { name: 'Notifications' })).toBeInTheDocument();

      await vi.advanceTimersByTimeAsync(dataNoticesPollMs + 1_000);

      expect(await screen.findByRole('button', { name: 'Notifications, 1 unread' })).toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });
});
