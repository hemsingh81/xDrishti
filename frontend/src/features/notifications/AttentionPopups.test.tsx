import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { portfolioNoticesPollMs } from '@/shared/api';
import {
  buildDividendsNotice,
  buildReconnectNotice,
  buildReviewNotice,
  buildSyncFailingNotice,
  resolved,
} from '@/test/attentionFixtures';
import { buildPortfolioNotice } from '@/test/actionCenterFixtures';
import { buildDataNotice, buildAccountDetail, buildConnected, ids, summaryOf } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { findToast } from '@/test/toast';
import { AttentionPopups } from './components/AttentionPopups';
import { NotificationBell } from './components/NotificationBell';
import type { ReadStorage } from './lib/readState';

class FakeStorage implements ReadStorage {
  data = new Map<string, string>();
  getItem = (key: string) => this.data.get(key) ?? null;
  setItem = (key: string, value: string) => {
    this.data.set(key, value);
  };
}

type Notice = ReturnType<typeof buildReconnectNotice>;

/** The notices the server answers with; a test changes `current.items` to let the next poll see something else. */
function serve(items: Notice[], accounts = [buildAccountDetail({ connection: buildConnected() })]) {
  const current = { items };
  server.use(
    http.get('/api/accounts', () => HttpResponse.json(accounts.map(summaryOf))),
    http.get('/api/portfolio/notices', () => HttpResponse.json({ items: current.items })),
  );
  return current;
}

function renderApp(storage: ReadStorage | null = new FakeStorage()) {
  return renderRoutes([
    {
      path: '/',
      element: (
        <>
          <NotificationBell storage={storage} />
          <AttentionPopups storage={storage} />
        </>
      ),
    },
    { path: '/accounts', element: <p>Accounts page</p> },
    { path: '/portfolio', element: <p>Portfolio page</p> },
  ]);
}

const stack = () => screen.queryByRole('region', { name: 'Notifications' });
/** The toasts on the page: every alert and status in the stack. */
const toasts = () => {
  const region = stack();
  return region ? [...within(region).queryAllByRole('alert'), ...within(region).queryAllByRole('status')] : [];
};
/** The bell has its count, so the notices have arrived (and the pop-ups have had their say). */
const bell = (name: string) => screen.findByRole('button', { name, hidden: true });
const poll = (ms = portfolioNoticesPollMs + 1_000) => act(() => vi.advanceTimersByTimeAsync(ms));

afterEach(() => {
  vi.useRealTimers();
});

describe('Attention pop-ups: what pops up', () => {
  it('shows an item that needs the owner as a toast with its title, text and buttons, once the notices are read', async () => {
    serve([buildSyncFailingNotice()]);
    renderApp();

    const toast = await findToast();

    expect(toast).toHaveAttribute('role', 'alert');
    expect(toast).toHaveTextContent('Trades are not being read');
    expect(toast).toHaveTextContent(
      'The trades of Test Dhan Main have not been updated for 2 days: Dhan could not be reached.',
    );
    expect(
      within(toast)
        .getAllByRole('button')
        .map((b) => b.textContent),
    ).toEqual(['Retry now', 'Open', '']);
    expect(within(toast).getByRole('button', { name: 'Close notification' })).toBeInTheDocument();
  });

  it('shows a warning and information as such, with a countdown, and lets them go by themselves', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    serve([buildDividendsNotice()]);
    renderApp();

    const toast = await findToast();
    expect(toast).toHaveAttribute('role', 'status');
    expect(toast).toHaveTextContent('Possible dividends in the ledger');
    expect(toast.querySelector('[data-countdown]')).not.toBeNull();

    await poll(10_500);
    await waitFor(() => {
      expect(stack()).toBeNull();
    });
    // It is still in the bell.
    expect(await bell('Notifications, 1 unread')).toBeInTheDocument();
  });

  it('keeps an error until it is closed, however long that takes', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    serve([buildReconnectNotice()]);
    renderApp();

    const toast = await findToast();
    expect(toast.querySelector('[data-countdown]')).toBeNull();
    await poll(5 * 60_000);

    expect(toasts()).toHaveLength(1);
    await userEvent.click(within(toasts()[0]!).getByRole('button', { name: 'Close notification' }));
    await waitFor(() => {
      expect(stack()).toBeNull();
    });
    // Closing it does not read it.
    expect(await bell('Notifications, 1 unread')).toBeInTheDocument();
  });

  it('pops up the API-key expiry that the app derives itself, with Open as its button', async () => {
    const soon = new Date(Date.now() + 5 * 86_400_000).toISOString().slice(0, 10);
    serve([], [buildAccountDetail({ connection: buildConnected(), apiKeyExpiresOn: soon })]);
    renderApp();

    const toast = await findToast();

    expect(toast).toHaveTextContent('Test Dhan Main API key expires on');
    expect(within(toast).getByRole('button', { name: 'Open' })).toBeInTheDocument();
  });

  it('leaves the other notices (data, services, alerts) to the bell', async () => {
    serve([]);
    server.use(http.get('/api/data/notices', () => HttpResponse.json({ items: [buildDataNotice()] })));
    renderApp();

    expect(await bell('Notifications, 1 unread')).toBeInTheDocument();
    expect(stack()).toBeNull();
  });

  it('does not pop up what is already resolved', async () => {
    serve([resolved(buildReconnectNotice())]);
    renderApp();

    await bell('Notifications');
    expect(stack()).toBeNull();
  });
});

describe('Attention pop-ups: once only', () => {
  it('does not show an item again after a reload, and still lists it unread in the bell', async () => {
    const storage = new FakeStorage();
    serve([buildReconnectNotice()]);
    const first = renderApp(storage);
    await findToast();
    first.unmount();

    serve([buildReconnectNotice()]);
    renderApp(storage);

    expect(await bell('Notifications, 1 unread')).toBeInTheDocument();
    expect(stack()).toBeNull();
  });

  it('shows a changed situation (a new id) again', async () => {
    const storage = new FakeStorage();
    serve([buildSyncFailingNotice()]);
    const first = renderApp(storage);
    await findToast();
    first.unmount();

    serve([buildSyncFailingNotice({ id: `attention:sync:${ids.main}:trades:rate_limited` })]);
    renderApp(storage);

    expect(await findToast()).toHaveTextContent('Trades are not being read');
  });

  it('does not show an item twice while the app stays open, however often the notices are read', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    serve([buildReconnectNotice()]);
    renderApp();
    await userEvent.click(within(await findToast()).getByRole('button', { name: 'Close notification' }));
    await waitFor(() => {
      expect(stack()).toBeNull();
    });

    await poll();
    await poll();

    expect(stack()).toBeNull();
  });

  it('shows an item that appears while the app is open, even when others are waiting in the bell', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const server_ = serve([buildReconnectNotice()]);
    renderApp();
    expect(await findToast()).toHaveTextContent('Reconnect Dhan');

    server_.items = [buildSyncFailingNotice(), buildReconnectNotice()];
    await poll();

    await waitFor(() => {
      expect(toasts()).toHaveLength(2);
    });
    expect(toasts()[1]).toHaveTextContent('Trades are not being read');
  });
});

describe('Attention pop-ups: when the app opens', () => {
  it('shows only the most urgent unread item; the others wait in the bell and do not come one by one on later reloads', async () => {
    const storage = new FakeStorage();
    const items = [buildDividendsNotice(), buildReviewNotice(), buildReconnectNotice()];
    serve(items);
    const first = renderApp(storage);

    const toast = await findToast();
    expect(toast).toHaveTextContent('Reconnect Dhan');
    expect(await bell('Notifications, 3 unread')).toBeInTheDocument();
    expect(toasts()).toHaveLength(1);
    first.unmount();

    serve(items);
    renderApp(storage);
    expect(await bell('Notifications, 3 unread')).toBeInTheDocument();
    expect(stack()).toBeNull();
  });

  it('picks the newest among items of the same severity', async () => {
    serve([
      buildReviewNotice({ at: '2026-10-13T10:00:00Z' }),
      buildReviewNotice({
        id: 'attention:review:newer',
        title: 'Newer review',
        at: '2026-10-14T10:00:00Z',
      }),
    ]);
    renderApp();

    expect(await findToast()).toHaveTextContent('Newer review');
    expect(toasts()).toHaveLength(1);
  });

  it('does not show an item the owner has read already', async () => {
    const storage = new FakeStorage();
    const notice = buildReconnectNotice();
    storage.setItem('xd.notifications.read', JSON.stringify([`${notice.id}|${notice.since ?? ''}`]));
    serve([notice]);
    renderApp(storage);

    await bell('Notifications');
    expect(stack()).toBeNull();
  });

  it('still honours a plain id stored before items were told apart by time, for an item that has no time', async () => {
    const storage = new FakeStorage();
    const notice = buildReconnectNotice({ since: null });
    storage.setItem('xd.notifications.read', JSON.stringify([notice.id]));
    serve([notice]);
    renderApp(storage);

    await bell('Notifications');
    expect(stack()).toBeNull();
  });

  it('does not let a stored plain id hide an item that has a time: it is a situation of its own', async () => {
    const storage = new FakeStorage();
    const notice = buildReconnectNotice();
    storage.setItem('xd.notifications.read', JSON.stringify([notice.id]));
    storage.setItem('xd.notifications.popped', JSON.stringify([notice.id]));
    serve([notice]);
    renderApp(storage);

    expect(await findToast()).toHaveTextContent('Reconnect Dhan');
    expect(await bell('Notifications, 1 unread')).toBeInTheDocument();
  });
});

describe('Attention pop-ups: closing by themselves', () => {
  it('closes the pop-up of an error when its cause is gone and the item is resolved', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const notices = serve([buildReconnectNotice()]);
    renderApp();
    await findToast();

    notices.items = [resolved(buildReconnectNotice())];
    await poll();

    await waitFor(() => {
      expect(stack()).toBeNull();
    });
    expect(await bell('Notifications')).toBeInTheDocument();
  });

  it('closes it when the item is dropped from the notices', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const notices = serve([buildReconnectNotice()]);
    renderApp();
    await findToast();

    notices.items = [];
    await poll();

    await waitFor(() => {
      expect(stack()).toBeNull();
    });
  });

  it('closes it when the item is marked read in the bell', async () => {
    serve([buildReconnectNotice()]);
    renderApp();
    await findToast();

    await userEvent.click(await bell('Notifications, 1 unread'));
    const drawer = within(await screen.findByRole('dialog', { name: 'Notifications' }));
    await userEvent.click(drawer.getByRole('button', { name: 'Mark all read' }));

    await waitFor(() => {
      expect(stack()).toBeNull();
    });
  });
});

describe('Attention pop-ups: buttons', () => {
  it('marks the item read and goes to the page when a route button is pressed', async () => {
    serve([buildReconnectNotice()]);
    const { router } = renderApp();
    const toast = await findToast();

    await userEvent.click(within(toast).getByRole('button', { name: 'Reconnect Dhan' }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/accounts');
    });
    expect(router.state.location.search).toBe(`?account=${ids.main}&tab=connection`);
    await waitFor(() => {
      expect(stack()).toBeNull();
    });
    // The page it went to has no bell; the read state is in storage for the next one.
  });

  it('counts the item read after its button, so the bell has no unread left', async () => {
    const storage = new FakeStorage();
    serve([buildReviewNotice()]);
    const { router } = renderApp(storage);
    const toast = await findToast();
    expect(await bell('Notifications, 1 unread')).toBeInTheDocument();

    await userEvent.click(within(toast).getByRole('button', { name: 'Review' }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/portfolio');
    });
    expect(JSON.parse(storage.data.get('xd.notifications.read') ?? '[]')).toEqual([
      `${buildReviewNotice().id}|${buildReviewNotice().since ?? ''}`,
    ]);
  });

  it('runs a command button, then tells the result in a second toast', async () => {
    serve([buildSyncFailingNotice()]);
    const calls: string[] = [];
    server.use(
      http.post('/api/accounts/:id/sync/broker-history', ({ params }) => {
        calls.push(String(params.id));
        return HttpResponse.json({ id: 'run' }, { status: 202 });
      }),
    );
    renderApp();
    const toast = await findToast();

    await userEvent.click(within(toast).getByRole('button', { name: 'Retry now' }));

    await waitFor(() => {
      expect(calls).toEqual([ids.main]);
    });
    await waitFor(() => {
      expect(toasts().map((t) => t.textContent)).toEqual([
        expect.stringContaining('Retry now: started. This notice goes away by itself once it works.'),
      ]);
    });
  });
});

describe('Attention pop-ups: the same situation coming back', () => {
  const again = () => buildReconnectNotice({ since: '2026-10-15T09:30:00Z', at: '2026-10-15T09:30:00Z' });

  async function readEverything() {
    await userEvent.click(await bell('Notifications, 1 unread'));
    await userEvent.click(await screen.findByRole('button', { name: 'Mark all read' }));
    await userEvent.click(screen.getByRole('button', { name: 'Close notifications' }));
  }

  it('counts and pops up again when a resolved item is raised again with a newer time; an unchanged item does not', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const server_ = serve([buildReconnectNotice()]);
    renderApp();
    await userEvent.click(within(await findToast()).getByRole('button', { name: 'Close notification' }));
    await readEverything();
    expect(await bell('Notifications')).toBeInTheDocument();

    await poll();
    expect(await bell('Notifications')).toBeInTheDocument();
    expect(stack()).toBeNull();

    server_.items = [resolved(buildReconnectNotice())];
    await poll();
    expect(await bell('Notifications')).toBeInTheDocument();

    server_.items = [again()];
    await poll();

    expect(await findToast()).toHaveTextContent('Reconnect Dhan');
    expect(await bell('Notifications, 1 unread')).toBeInTheDocument();
  });

  it('does not pop the returned item up again after a reload', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const storage = new FakeStorage();
    const server_ = serve([buildReconnectNotice()]);
    const first = renderApp(storage);
    await userEvent.click(within(await findToast()).getByRole('button', { name: 'Close notification' }));
    server_.items = [resolved(buildReconnectNotice())];
    await poll();
    server_.items = [again()];
    await poll();
    expect(await findToast()).toHaveTextContent('Reconnect Dhan');
    first.unmount();

    serve([again()]);
    renderApp(storage);

    expect(await bell('Notifications, 1 unread')).toBeInTheDocument();
    expect(stack()).toBeNull();
  });
});

describe('Notices that are not attention items: a new `since` on every poll', () => {
  const stamped = (since: string) => buildPortfolioNotice({ since });

  it('stays read across polls, and the badge never comes back', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const server_ = serve([stamped('2026-10-15T09:00:00Z')]);
    renderApp();
    await userEvent.click(await bell('Notifications, 1 unread'));
    await userEvent.click(await screen.findByRole('button', { name: 'Mark all read' }));
    await userEvent.click(screen.getByRole('button', { name: 'Close notifications' }));
    expect(await bell('Notifications')).toBeInTheDocument();

    for (const since of ['2026-10-15T09:01:00Z', '2026-10-15T09:02:00Z']) {
      server_.items = [stamped(since)];
      await poll();
      expect(await bell('Notifications')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Notifications, 1 unread', hidden: true })).toBeNull();
    }
  });

  it('keeps an unread one counted once, not once per poll', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const server_ = serve([stamped('2026-10-15T09:00:00Z')]);
    renderApp();
    expect(await bell('Notifications, 1 unread')).toBeInTheDocument();

    server_.items = [stamped('2026-10-15T09:01:00Z')];
    await poll();

    expect(await bell('Notifications, 1 unread')).toBeInTheDocument();
  });
});
