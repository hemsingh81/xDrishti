import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { captureIntervals, passMinutes, setNow } from '@/test/clock';
import { createHubFake, priceOf, statusOf, type HubFake } from '@/test/hubFake';
import { isoAt, liveNow, serveLive, type LiveServer } from '@/test/liveFixtures';
import { server } from '@/test/server';
import { buildTimeline } from '@/test/servicesFixtures';
import { sessionQueryKey } from '@/features/auth';
import { renderPortfolio } from './testing';

let hub: HubFake;
let api: LiveServer;

beforeEach(() => {
  hub = createHubFake();
  hub.install();
  api = serveLive();
  setNow(liveNow);
});

/** The chip is found by what a screen reader hears: its text and what it means. */
const chip = (name: RegExp) => screen.findByRole('img', { name });
const noChip = (name: RegExp) => screen.queryByRole('img', { name });

async function ready() {
  const view = renderPortfolio();
  await screen.findByRole('row', { name: /^TCS\b/ });
  await waitFor(() => {
    expect(hub.subscribes).not.toHaveLength(0);
  });
  await waitFor(() => {
    expect(api.loads.base).toBeGreaterThan(0);
  });
  return view;
}

describe('Portfolio: the prices-line chip', () => {
  it('says Live with the time of the newest tick of the scope while ticks are fresh', async () => {
    await ready();
    hub.push([priceOf(2, 3920, isoAt('10:35:42'))], isoAt('10:35:43'));

    const live = await chip(/^Live · 10:35:42 IST\./);
    expect(live).toHaveAccessibleName(/Prices are live: the last tick for the shown holdings was at 10:35:42 IST/);
    expect(screen.getByText(/Prices move with the exchange feed/)).toBeInTheDocument();
    expect(live.closest('[aria-live], [role="status"], [role="alert"]')).toBeNull();
  });

  it('keeps saying Live, with the age, up to a minute after the last tick, then says Delayed with the age', async () => {
    await ready();
    hub.push([priceOf(2, 3920, isoAt('10:35:42'))], isoAt('10:35:43'));
    await chip(/^Live · 10:35:42 IST/);

    passMinutes(0.4); // 10:36:08 here, 10:36:07 on the server clock: 25 s after the tick
    expect(await chip(/^Live · last tick 25 s ago/)).toBeInTheDocument();

    passMinutes(0.8); // 10:36:56: 74 s
    expect(await chip(/^Delayed · 1 min\./)).toBeInTheDocument();
    expect(screen.getByText(/Last tick 10:35:42 IST/)).toBeInTheDocument();
    expect(noChip(/^Live/)).toBeNull();

    hub.push([priceOf(2, 3925, isoAt('10:36:56'))], isoAt('10:36:57'));
    expect(await chip(/^Live · 10:36:56 IST/)).toBeInTheDocument();
  });

  it('measures the age on the server clock, not the browser clock alone', async () => {
    await ready();
    // The browser is 5 minutes ahead of the server, which stamps its message 10:30:40.
    hub.push([priceOf(2, 3920, isoAt('10:30:38'))], isoAt('10:30:40'));

    expect(await chip(/^Live · 10:30:38 IST/)).toBeInTheDocument();
  });

  it('waits a minute for the first tick before calling the feed late', async () => {
    await ready();
    expect(await chip(/^Quotes\./)).toBeInTheDocument();

    passMinutes(1.2);
    expect(await chip(/^Delayed · no tick yet\./)).toBeInTheDocument();
  });

  it.each(['paused', 'no_token', 'token_refused', 'flapping', 'not_running', 'closed', 'idle'])(
    'says Quotes when the feed is %s',
    async (state) => {
      await ready();
      hub.push([priceOf(2, 3920, isoAt('10:35:42'))], isoAt('10:35:43'));
      await chip(/^Live/);

      hub.status(statusOf({ state }));

      expect(
        await chip(/^Quotes\. Prices come from the five-minute quotes; the live feed is not streaming/),
      ).toBeInTheDocument();
      expect(screen.getByText(/quotes every 5 min|Prices as of|Prices delayed/)).toBeInTheDocument();
    },
  );

  it('says Pre-open from 09:00 to the open, with the prices marked indicative', async () => {
    setNow('2026-10-14T09:05:00+05:30');
    renderPortfolio();

    expect(await chip(/^Pre-open\./)).toBeInTheDocument();
    expect(screen.getByText(/pre-open prices are indicative until the open/)).toBeInTheDocument();
  });

  it('says Closed outside the session and on a day without one', async () => {
    setNow('2026-10-14T16:00:00+05:30');
    const { unmount } = renderPortfolio();
    expect(await chip(/^Closed\./)).toBeInTheDocument();
    expect(screen.getByText(/^As of 14 Oct 10:35 IST/)).toBeInTheDocument();
    unmount();

    setNow(liveNow);
    server.use(
      http.get('/api/services/timeline', () =>
        HttpResponse.json(buildTimeline({ tradingDay: false, marketOpen: null, marketClose: null })),
      ),
    );
    renderPortfolio();
    expect(await chip(/^Closed\./)).toBeInTheDocument();
  });
});

describe('Portfolio: without the hub', () => {
  it('falls back to the once-a-minute read, says so, shows no error, and reads the REST status of the feed', async () => {
    hub.behaviour = 'refuse';
    let statusReads = 0;
    server.use(
      http.get('/api/live/status', () => {
        statusReads += 1;
        return HttpResponse.json(statusOf({ state: 'not_running', subscribed: 0 }));
      }),
    );
    const timers = captureIntervals(60_000, 300_000);
    renderPortfolio();
    await screen.findByRole('row', { name: /^TCS\b/ });

    expect(
      await chip(/^Quotes\. Prices come from the five-minute quotes; live updates are not available/),
    ).toBeInTheDocument();
    expect(screen.getByText('Live updates are not available: reading every minute.')).toBeInTheDocument();
    await waitFor(() => {
      expect(statusReads).toBeGreaterThan(0);
    });
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    const before = { ...api.loads };
    expect(timers.count(300_000)).toBe(0);
    timers.fire(60_000);
    await waitFor(() => {
      expect(api.loads.list).toBeGreaterThan(before.list);
      expect(api.loads.summary).toBeGreaterThan(before.summary);
    });
    expect(hub.attempts).toBeGreaterThan(1);
  });

  it('reads once when the hub returns and goes back to the 5-minute cadence and the Live chip', async () => {
    hub.behaviour = 'refuse';
    const timers = captureIntervals(60_000, 300_000);
    renderPortfolio();
    await screen.findByRole('row', { name: /^TCS\b/ });
    await chip(/live updates are not available/);
    await waitFor(() => {
      expect(hub.attempts).toBeGreaterThan(1);
    });
    const before = { ...api.loads };

    hub.behaviour = 'connect';

    await waitFor(() => {
      expect(hub.subscribes).not.toHaveLength(0);
    });
    await waitFor(() => {
      expect(api.loads.list).toBe(before.list + 1);
      expect(api.loads.summary).toBe(before.summary + 1);
    });
    await waitFor(() => {
      expect(timers.count(300_000)).toBeGreaterThan(0);
    });
    hub.push([priceOf(2, 3920, isoAt('10:35:42'))], isoAt('10:35:43'));
    expect(await chip(/^Live · 10:35:42 IST/)).toBeInTheDocument();
    expect(screen.queryByText('Live updates are not available: reading every minute.')).not.toBeInTheDocument();
  });
});

describe('Portfolio: a session that ended while the hub was connected', () => {
  it('is met by the normal session handling through the status read, without a dialog', async () => {
    const { client } = await ready();
    server.use(
      http.get('/api/live/status', () =>
        HttpResponse.json(
          { title: 'Your session has expired.', status: 401, code: 'auth.session_expired' },
          { status: 401 },
        ),
      ),
    );
    hub.behaviour = 'refuse';

    hub.drop();

    await waitFor(() => {
      expect(client.getQueryData(sessionQueryKey)).toEqual({ status: 'anonymous', reason: 'expired' });
    });
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });
});
