import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { servicesPortKeys } from '@/shared/api';
import { captureIntervals, captureTimeouts, passMinutes, setNow } from '@/test/clock';
import { createHubFake, priceOf, statusOf, type HubFake } from '@/test/hubFake';
import { ids } from '@/test/fixtures';
import type { RoutesRender } from '@/test/render';
import { at, isoAt, liveHoldings, liveNow, serveLive, type LiveServer } from '@/test/liveFixtures';
import { stubReducedMotion } from '@/test/matchMedia';
import { portfolioKeys } from './api/portfolioQueries';
import { quoteReadDelayMs } from './hooks/useLiveFeed';
import { renderPortfolio } from './testing';

let hub: HubFake;
let api: LiveServer;

beforeEach(() => {
  hub = createHubFake();
  hub.install();
  api = serveLive();
  setNow(liveNow);
});

const row = (symbol: string) => screen.findByRole('row', { name: new RegExp(`^${symbol}\\b`) });
const cell = (element: HTMLElement, index: number) => within(element).getAllByRole('gridcell')[index]!;
/** The tile that carries a label, e.g. "Current value". */
const tile = async (label: string) => {
  const card = (await screen.findByText(label, { selector: 'p' })).closest('.MuiCard-root');
  return within(card as HTMLElement);
};

/**
 * The market's timeline is in the cache and the page has rendered with it: from here on the page knows whether the market is
 * on, so "the hub was not asked for" can be checked without waiting for a time to pass.
 */
async function marketKnown({ client }: Pick<RoutesRender, 'client'>) {
  await waitFor(() => {
    expect(client.getQueriesData({ queryKey: servicesPortKeys.timeline }).some(([, data]) => data !== undefined)).toBe(
      true,
    );
  });
  // The query cache tells its observers in a task of its own; this one is queued after it.
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

/** The page is up, the hub follows it and the holdings the totals are made of are read. */
async function following(search = '') {
  const view = renderPortfolio(search);
  await (search.includes('groupBy') ? screen.findByRole('table', { name: /^Holdings by/ }) : row('TCS'));
  await waitFor(() => {
    expect(hub.subscribes).not.toHaveLength(0);
  });
  await waitFor(() => {
    expect(api.loads.base).toBeGreaterThan(0);
  });
  return view;
}

describe('Portfolio: the live hub is open while the market is on and the page is shown', () => {
  it('connects in the session and subscribes with no account (all that count in totals) and the holdings shown', async () => {
    await following();

    expect(hub.lastScope).toMatchObject({ accountId: null, bucketId: null });
    expect(hub.lastScope?.holdingIds).toHaveLength(3);
  });

  it('follows the open holding even when the grid does not show it (a grouped view)', async () => {
    const id = '00000000-0000-4000-8000-0000000c0002';
    renderPortfolio(`?groupBy=sector&holding=${id}`);

    await waitFor(() => {
      expect(hub.lastScope?.holdingIds).toEqual([id]);
    });
  });

  it('connects in pre-open (09:00 to the open) too', async () => {
    setNow('2026-10-14T09:05:00+05:30');
    renderPortfolio();
    await row('TCS');

    await waitFor(() => {
      expect(hub.subscribes).toHaveLength(1);
    });
  });

  it.each([
    ['before pre-open', '2026-10-14T08:59:00+05:30'],
    ['after the close', '2026-10-14T15:31:00+05:30'],
  ])('does not connect %s', async (_name, when) => {
    setNow(when);
    const view = renderPortfolio();
    await row('TCS');
    await marketKnown(view);

    expect(hub.attempts).toBe(0);
  });

  it('does not connect on a day without a session', async () => {
    const { http, HttpResponse } = await import('msw');
    const { server } = await import('@/test/server');
    const { buildTimeline } = await import('@/test/servicesFixtures');
    server.use(
      http.get('/api/services/timeline', () =>
        HttpResponse.json(buildTimeline({ tradingDay: false, marketOpen: null, marketClose: null })),
      ),
    );
    const view = renderPortfolio();
    await row('TCS');
    await marketKnown(view);

    expect(hub.attempts).toBe(0);
  });

  it('does not connect in a hidden tab, and does when the tab is shown', async () => {
    const hidden = vi.spyOn(document, 'hidden', 'get').mockReturnValue(true);
    const view = renderPortfolio();
    await row('TCS');
    await marketKnown(view);
    expect(hub.attempts).toBe(0);

    hidden.mockReturnValue(false);
    document.dispatchEvent(new Event('visibilitychange'));
    await waitFor(() => {
      expect(hub.subscribes).toHaveLength(1);
    });
  });

  it('subscribes again when the account changes, and unsubscribes when the page is left', async () => {
    const { router, unmount } = await following();
    expect(hub.subscribes).toHaveLength(1);

    await router.navigate('/portfolio?account=00000000-0000-4000-8000-000000000001');
    await waitFor(() => {
      expect(hub.lastScope?.accountId).toBe('00000000-0000-4000-8000-000000000001');
    });
    expect(hub.subscribes.length).toBeGreaterThan(1);

    unmount();
    await waitFor(() => {
      expect(hub.unsubscribes).toBe(1);
    });
  });
});

describe('Portfolio: prices pushed over the hub', () => {
  it('move the row, the tiles and the group row, in the order the page was read', async () => {
    await following();
    const tcs = await row('TCS');
    expect(cell(tcs, 4)).toHaveTextContent('₹3,880.00');
    expect((await tile('Current value')).getByText('₹73,800')).toBeInTheDocument();

    hub.push([priceOf(2, 3920, isoAt('10:35:42'))], new Date(Date.parse(isoAt('10:35:43'))).toISOString());

    await waitFor(() => {
      expect(cell(tcs, 4)).toHaveTextContent('₹3,920.00');
    });
    expect(cell(tcs, 7)).toHaveTextContent('₹39,200');
    expect(cell(tcs, 8)).toHaveTextContent('▼−₹800');
    expect(cell(tcs, 5)).toHaveTextContent('▲+₹200');
    expect((await tile('Current value')).getByText('₹74,200')).toBeInTheDocument();
    expect((await tile('Day change')).getByText('+₹600')).toBeInTheDocument();
  });

  it('stay when the scope changes: a resubscribe sends only new instruments, so the screen keeps what it was given', async () => {
    const { router } = await following();
    const tcs = await row('TCS');
    hub.push([priceOf(2, 3920, isoAt('10:35:42'))], isoAt('10:35:43'));
    await waitFor(() => {
      expect(cell(tcs, 4)).toHaveTextContent('₹3,920.00');
    });
    const subscribes = hub.subscribes.length;

    // Another scope: the server answers with no snapshot of instruments it already sent on this connection.
    await router.navigate('/portfolio?account=00000000-0000-4000-8000-000000000001');
    await waitFor(() => {
      expect(hub.subscribes.length).toBeGreaterThan(subscribes);
    });

    expect(cell(await row('TCS'), 4)).toHaveTextContent('₹3,920.00');
    expect((await tile('Current value')).getByText('₹74,200')).toBeInTheDocument();
  });

  it('are ignored when older than the price on the row', async () => {
    await following();
    const tcs = await row('TCS');
    const reliance = await row('RELIANCE');

    // The older price of TCS and a newer one of RELIANCE arrive in one message: once RELIANCE moved, TCS has been decided.
    hub.push([priceOf(2, 4100, isoAt('10:35:00')), priceOf(1, 2950, isoAt('10:35:50'))]);

    await waitFor(() => {
      expect(cell(reliance, 4)).toHaveTextContent('₹2,950.00');
    });
    expect(cell(tcs, 4)).toHaveTextContent('₹3,880.00');
  });
});

describe('Portfolio: reading the figures again while live', () => {
  it('reads the list and the totals every 5 minutes, not every minute', async () => {
    const timers = captureIntervals(60_000, 300_000);
    const { client } = await following();

    // The one-minute timers that remain belong to other cards; they do not read the list or the totals. A read starts the
    // moment its timer fires, so nothing being fetched right after is the proof.
    timers.fire(60_000);
    expect(client.isFetching({ queryKey: portfolioKeys.holdings })).toBe(0);
    expect(client.isFetching({ queryKey: portfolioKeys.summary })).toBe(0);

    const before = { ...api.loads };
    expect(timers.count(300_000)).toBeGreaterThan(0);
    timers.fire(300_000);
    await waitFor(() => {
      expect(api.loads.list).toBeGreaterThan(before.list);
      expect(api.loads.summary).toBeGreaterThan(before.summary);
      expect(api.loads.base).toBeGreaterThan(before.base);
    });
  });

  it('reads the figures once more when the hub comes back after a drop, but not the holdings behind the totals while they are fresh', async () => {
    const { client } = await following();
    const before = { ...api.loads };

    hub.drop();
    await waitFor(() => {
      expect(hub.subscribes.length).toBeGreaterThan(1);
    });

    await waitFor(() => {
      expect(api.loads.list).toBeGreaterThan(before.list);
      expect(api.loads.summary).toBeGreaterThan(before.summary);
    });
    await waitFor(() => {
      expect(client.isFetching()).toBe(0);
    });
    expect(api.loads.base).toBe(before.base);
    expect(hub.lastScope).toMatchObject({ accountId: null });
  });

  it('reads the holdings behind the totals again when the hub comes back after more than 5 minutes', async () => {
    await following();
    const before = { ...api.loads };

    passMinutes(6);
    hub.drop();

    await waitFor(() => {
      expect(api.loads.base).toBeGreaterThan(before.base);
    });
  });
});

describe('Portfolio: totals and holdings that do not fit each other', () => {
  const extra = () => ({
    ...liveHoldings()[0]!,
    id: '00000000-0000-4000-8000-0000000c0009',
    symbol: 'NEWCO',
    instrumentId: null,
  });

  it('reads the older of the two again, once, when a holding appeared between the reads', async () => {
    const { client } = await following();
    const tcs = await row('TCS');
    // A first price for TCS, so the next quote is news.
    hub.push([priceOf(2, 3920, isoAt('10:35:42'))], isoAt('10:35:43'));
    await waitFor(() => {
      expect(cell(tcs, 4)).toHaveTextContent('₹3,920.00');
    });
    const before = { ...api.loads };

    // The server now has four open holdings; a quote makes the totals and the list read again, the holdings behind the live
    // totals (still three) are not read again by that.
    api.holdings = [...api.holdings, extra()];
    hub.push([priceOf(2, 3925, isoAt('10:40:00'), 'quote')], isoAt('10:40:01'));

    await waitFor(
      () => {
        expect(api.loads.summary).toBeGreaterThan(before.summary);
        expect(api.loads.base).toBeGreaterThan(before.base);
      },
      { timeout: 4000 },
    );
    await waitFor(() => {
      expect(client.isFetching()).toBe(0);
    });
    // They fit now: no further read.
    const after = api.loads.base;
    await screen.findByText('Current value', { selector: 'p' });
    expect(api.loads.base).toBe(after);
    expect(api.loads.base - before.base).toBe(1);
  });
});

describe('Portfolio: the hub follows the market', () => {
  it('is closed when the market closes during the session (the close itself, 15:30, is still open)', async () => {
    setNow('2026-10-14T15:30:00+05:30');
    await following();
    expect(hub.alive).toBe(true);
    expect(screen.queryByRole('img', { name: /^Closed\./ })).not.toBeInTheDocument();

    passMinutes(1);

    await waitFor(() => {
      expect(hub.alive).toBe(false);
    });
    expect(hub.unsubscribes).toBe(1);
    expect(await screen.findByRole('img', { name: /^Closed\./ })).toBeInTheDocument();
  });
});

describe('Portfolio: a price that changed flashes', () => {
  it('marks the price cell for a second with an arrow and a tint, and clears it', async () => {
    await following();
    const tcs = await row('TCS');

    hub.push([priceOf(2, 3920, isoAt('10:35:42'))]);

    await waitFor(() => {
      expect(cell(tcs, 4).querySelector('[data-flash]')).toHaveAttribute('data-flash', 'up');
    });
    expect(cell(tcs, 4)).toHaveTextContent('▲₹3,920.00');

    hub.push([priceOf(2, 3900, isoAt('10:35:43'))]);
    await waitFor(() => {
      expect(cell(tcs, 4).querySelector('[data-flash]')).toHaveAttribute('data-flash', 'down');
    });
    expect(cell(tcs, 4)).toHaveTextContent('▼₹3,900.00');

    await waitFor(
      () => {
        expect(cell(tcs, 4).querySelector('[data-flash]')).toBeNull();
      },
      { timeout: 2500 },
    );
    expect(cell(tcs, 4)).toHaveTextContent('₹3,900.00');
  });

  it('does not mark anything under prefers-reduced-motion', async () => {
    stubReducedMotion();
    await following();
    const tcs = await row('TCS');

    hub.push([priceOf(2, 3920, isoAt('10:35:42'))]);

    await waitFor(() => {
      expect(cell(tcs, 4)).toHaveTextContent('₹3,920.00');
    });
    expect(cell(tcs, 4).querySelector('[data-flash]')).toBeNull();
    expect(cell(tcs, 4)).not.toHaveTextContent('▲');
    expect(cell(tcs, 4)).not.toHaveTextContent('▼');
  });
});

describe('Portfolio: the page keeps its place while prices move', () => {
  it('keeps the sort, the order and scroll position of the rows and the open drawer', async () => {
    const { router } = await following('?sort=-value');
    // The drawer hides the page from the accessibility tree, so the rows are read from the document.
    const rows = () => [...document.querySelectorAll('[role="row"]')].map((r) => r.textContent.slice(0, 8));
    const tcs = await row('TCS');
    const order = rows();

    // The rows scroll inside the grid's own container; jsdom has no layout, but it keeps the position it is given.
    const scroller = screen.getByRole('grid', { name: 'Holdings' }).parentElement!;
    scroller.scrollTop = 40;
    expect(scroller.scrollTop).toBe(40);

    await userEvent.click(tcs);
    const drawer = await screen.findByLabelText('Holding details');
    expect(router.state.location.search).toContain('holding=');

    hub.push([priceOf(1, 3500, isoAt('10:35:42')), priceOf(2, 3000, isoAt('10:35:42'))]);

    await waitFor(() => {
      expect(within(drawer).getByText('Last price', { selector: 'dt' }).nextElementSibling).toHaveTextContent(
        '₹3,000.00',
      );
    });
    expect(rows()).toEqual(order);
    expect(scroller.isConnected).toBe(true);
    expect(scroller.scrollTop).toBe(40);
    expect(router.state.location.search).toContain('sort=-value');
    expect(screen.getByLabelText('Holding details')).toBe(drawer);
    expect(within(drawer).getByText('Price source', { selector: 'dt' }).nextElementSibling).toHaveTextContent(
      'Live tick',
    );
    expect(within(drawer).getByText('Price time (IST)', { selector: 'dt' }).nextElementSibling).toHaveTextContent(
      '10:35:42 IST',
    );
  });
});

describe('Portfolio: holdings that are not live', () => {
  it('says how many have no live tick, and the drawer says why', async () => {
    await following();

    // RELIANCE is at a quote, SMALLCO is not in the master list; TCS ticked today.
    expect(await screen.findByText(/2 holdings are not live/)).toBeInTheDocument();

    await userEvent.click(await row('SMALLCO'));
    const drawer = await screen.findByLabelText('Holding details');
    expect(within(drawer).getByText('Live price', { selector: 'dt' }).nextElementSibling).toHaveTextContent(
      /Not in the master list/,
    );
    expect(within(drawer).getByText('Price source', { selector: 'dt' }).nextElementSibling).toHaveTextContent('Synced');
  });

  it('says when the server follows fewer prices than the screen has', async () => {
    hub.ack = { subscribed: 2, truncated: true, failed: false };
    await following();

    expect(
      await screen.findByText(/2 holdings are not live.*a connection follows at most 1,000 prices/),
    ).toBeInTheDocument();
  });

  it('counts a holding as live once its first tick arrives', async () => {
    await following();
    await screen.findByText(/2 holdings are not live/);

    hub.push([priceOf(1, 2910, isoAt('10:35:45'))]);

    expect(await screen.findByText(/1 holding is not live/)).toBeInTheDocument();
  });

  it('is not said when the feed is not streaming', async () => {
    hub.greeting = statusOf({ state: 'paused' });
    await following();

    // A pushed price reaches the totals only once the holdings behind them are read, and the note is made from the same rows.
    hub.push([priceOf(2, 3920, isoAt('10:35:42'))]);
    expect((await tile('Current value')).getByText('₹74,200')).toBeInTheDocument();
    expect(screen.queryByText(/not live/)).not.toBeInTheDocument();
  });
});

describe('Portfolio: the group rows move with the prices too', () => {
  it.each([
    ['sector', '₹67,800', '₹68,200'],
    ['account', '₹73,800', '₹74,200'],
  ])('worked out from the holdings of each group, grouped by %s', async (by, before, after) => {
    await following(`?groupBy=${by}`);
    const table = await screen.findByRole('table', { name: `Holdings by ${by}` });
    const group = (await within(table).findByText(by === 'sector' ? 'IT' : ids.main)).closest('tr') as HTMLElement;
    expect(group).toHaveTextContent(before);
    await waitFor(() => {
      expect(api.loads.base).toBeGreaterThan(by === 'sector' ? 1 : 0);
    });

    hub.push([priceOf(2, 3920, isoAt('10:35:42'))], isoAt('10:35:43'));

    await waitFor(() => {
      expect(group).toHaveTextContent(after);
    });
    // The total row of the table, and the share of the group in it.
    expect(within(table).getAllByText('₹74,200').length).toBeGreaterThan(0);
  });
});

describe('Portfolio: a quote arriving over the hub', () => {
  /** The server after TCS was quoted at 10:40: a new price, and a previous close written with it. */
  const quotedTcs = () =>
    api.holdings.map((h) =>
      h.symbol === 'TCS'
        ? {
            ...h,
            pricePaise: 392_500,
            priceAt: at('10:40:00'),
            source: 'quote' as const,
            previousClosePaise: 380_000,
            previousCloseAt: at('10:40:00'),
          }
        : h,
    );

  it('makes the page read its figures again, because a quote also writes the previous close, but not the holdings behind the live totals again for the same instrument', async () => {
    const timeouts = captureTimeouts(quoteReadDelayMs);
    const { client } = await following();
    hub.push([priceOf(2, 3920, isoAt('10:35:42'))], isoAt('10:35:43'));
    await waitFor(() => {
      expect(api.loads.list).toBeGreaterThan(0);
    });
    // The first quote of the day for TCS (its close may be new): everything is read, and that is not what is checked here.
    hub.push([priceOf(2, 3925, isoAt('10:38:00'), 'quote')], isoAt('10:38:01'));
    timeouts.fire(quoteReadDelayMs);
    await waitFor(() => {
      expect(client.isFetching()).toBe(0);
    });
    await waitFor(() => {
      expect(api.loads.base).toBeGreaterThan(0);
    });
    const before = { ...api.loads };

    hub.push([priceOf(2, 3926, isoAt('10:40:00'), 'quote')], isoAt('10:40:01'));
    // The page waits a moment so that the other writes of the quote are in; the test does not.
    expect(timeouts.count(quoteReadDelayMs)).toBe(1);
    timeouts.fire(quoteReadDelayMs);

    await waitFor(() => {
      expect(api.loads.list).toBeGreaterThan(before.list);
      expect(api.loads.summary).toBeGreaterThan(before.summary);
    });
    await waitFor(() => {
      expect(client.isFetching()).toBe(0);
    });
    expect(api.loads.base).toBe(before.base);
  });

  it("reads the holdings behind the live totals again for the first quote of the day, so the day change is the server's", async () => {
    const timeouts = captureTimeouts(quoteReadDelayMs);
    await following();
    const before = { ...api.loads };

    // The server wrote TCS's quote and the close that came with it; the push carries only the price.
    api.holdings = quotedTcs();
    hub.push([priceOf(2, 3925, isoAt('10:40:00'), 'quote')], isoAt('10:40:01'));
    timeouts.fire(quoteReadDelayMs);

    // 10 x (2,900 - 2,880) + 10 x (3,925 - 3,800) + 100 x (60 - 58); with the old close it would be 650.
    await waitFor(async () => {
      expect((await tile('Day change')).getByText('+₹1,650')).toBeInTheDocument();
    });
    expect(api.loads.base).toBeGreaterThan(before.base);
  });

  it('does so for the group rows too', async () => {
    const timeouts = captureTimeouts(quoteReadDelayMs);
    await following('?groupBy=account');
    const table = await screen.findByRole('table', { name: 'Holdings by account' });

    api.holdings = quotedTcs();
    hub.push([priceOf(2, 3925, isoAt('10:40:00'), 'quote')], isoAt('10:40:01'));
    timeouts.fire(quoteReadDelayMs);

    await waitFor(() => {
      expect((within(table).getByText(ids.main).closest('tr') as HTMLElement).textContent).toContain('+₹1,650');
    });
  });

  it('is not news when it is the first price of the instrument (the snapshot after a subscribe)', async () => {
    const timeouts = captureTimeouts(quoteReadDelayMs);
    await following();

    hub.push([priceOf(1, 2900, isoAt('10:30:00'), 'quote'), priceOf(3, 60, isoAt('10:29:00'), 'sync')]);

    expect(timeouts.count(quoteReadDelayMs)).toBe(0);
  });
});
