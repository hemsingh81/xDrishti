import { EventsHub, type EventsHubOptions, type HubTransport } from './eventsHub';
import type { LiveFeedStatus, LiveScope, PricesChanged, SubscribeAck } from './hubContract.generated';

const options: EventsHubOptions = { retryDelaysMs: [100, 200, 400], random: () => 0.5, resubscribeMs: 1000 };
const scope = (accountId: string | null = null, holdingIds: string[] | null = null): LiveScope => ({
  accountId,
  bucketId: null,
  holdingIds,
});

/** One connection as the hub sees it, with the server's side in the hands of the test. */
interface Link {
  transport: HubTransport;
  subscribes: LiveScope[];
  unsubscribes: number;
  stopped: boolean;
  prices: (message: PricesChanged) => void;
  status: (message: LiveFeedStatus) => void;
  close: () => void;
}

function setup(failures = 0, ack: SubscribeAck = { subscribed: 2, truncated: false, failed: false }) {
  const links: Link[] = [];
  let left = failures;
  const state = { ack, attempts: 0, subscribeThrows: 0, unsubscribeThrows: 0 };
  const factory = () => {
    state.attempts += 1;
    const failing = left > 0;
    if (failing) left -= 1;
    const link: Link = {
      subscribes: [],
      unsubscribes: 0,
      stopped: false,
      prices: () => undefined,
      status: () => undefined,
      close: () => undefined,
      transport: {
        start: () => (failing ? Promise.reject(new Error('refused')) : Promise.resolve()),
        stop: () => {
          link.stopped = true;
          return Promise.resolve();
        },
        subscribe: (wanted) => {
          link.subscribes.push(wanted);
          if (state.subscribeThrows > 0) {
            state.subscribeThrows -= 1;
            return Promise.reject(new Error('invocation failed'));
          }
          return Promise.resolve(state.ack);
        },
        unsubscribe: () => {
          link.unsubscribes += 1;
          if (state.unsubscribeThrows > 0) {
            state.unsubscribeThrows -= 1;
            return Promise.reject(new Error('invocation failed'));
          }
          return Promise.resolve();
        },
        onPricesChanged: (handler) => {
          link.prices = handler;
        },
        onFeedStatus: (handler) => {
          link.status = handler;
        },
        onClose: (handler) => {
          link.close = handler;
        },
      },
    };
    links.push(link);
    return Promise.resolve(link.transport);
  };
  const hub = new EventsHub(factory, options);
  const last = () => links.at(-1)!;
  return { hub, links, last, state };
}

const settle = () => vi.advanceTimersByTimeAsync(0);

beforeEach(() => {
  vi.useFakeTimers();
});
afterEach(() => {
  vi.useRealTimers();
});

describe('EventsHub', () => {
  it('opens one connection for any number of holders and closes it when the last lets go', async () => {
    const { hub, links, last } = setup();
    const first = hub.acquire();
    const second = hub.acquire();
    await settle();

    expect(links).toHaveLength(1);
    expect(hub.getSnapshot().connection).toBe('connected');

    first();
    await settle();
    expect(hub.getSnapshot().connection).toBe('connected');

    second();
    await settle();
    expect(last().stopped).toBe(true);
    expect(hub.getSnapshot().connection).toBe('idle');
  });

  it('does not open the connection until a holder asks (and not for a holder that gave up at once)', async () => {
    const { hub, links } = setup();
    expect(hub.getSnapshot().connection).toBe('idle');
    hub.acquire()();
    await settle();
    expect(links.every((link) => link.stopped)).toBe(true);
    expect(hub.getSnapshot().connection).toBe('idle');
  });

  it('sends the wanted scope once connected, sends a changed one, and not the same twice', async () => {
    const { hub, last } = setup();
    hub.setScope(scope('a'));
    hub.acquire();
    await settle();
    expect(last().subscribes).toEqual([scope('a')]);
    expect(hub.getSnapshot().ack).toEqual({ subscribed: 2, truncated: false, failed: false });

    hub.setScope(scope('a'));
    await settle();
    expect(last().subscribes).toHaveLength(1);

    hub.setScope(scope('a', ['h1']));
    await settle();
    expect(last().subscribes).toEqual([scope('a'), scope('a', ['h1'])]);
  });

  it('keeps following through a resubscribe: the state, the answer and the time following began stay, and prices keep coming', async () => {
    const { hub, last } = setup();
    const received: PricesChanged[] = [];
    hub.onPrices((message) => received.push(message));
    hub.setScope(scope('a'));
    hub.acquire();
    await settle();
    const since = hub.getSnapshot().ackAt;
    expect(since).not.toBeNull();
    await vi.advanceTimersByTimeAsync(5000);

    // The server sends only the instruments that are new to this connection; the client needs no snapshot to go on.
    hub.setScope(scope('a', ['h1']));
    expect(hub.getSnapshot().ack).not.toBeNull();
    await settle();
    expect(last().subscribes).toHaveLength(2);
    expect(hub.getSnapshot().connection).toBe('connected');
    expect(hub.getSnapshot().ackAt).toBe(since);

    last().prices({ asOf: '2026-10-14T05:00:00Z', instruments: [] });
    expect(received).toHaveLength(1);
  });

  it('unsubscribes when the scope is dropped, and before closing a connection that follows something', async () => {
    const { hub, last } = setup();
    const lease = hub.acquire();
    hub.setScope(scope());
    await settle();

    hub.setScope(null);
    await settle();
    expect(last().unsubscribes).toBe(1);
    expect(hub.getSnapshot().ack).toBeNull();

    hub.setScope(scope('b'));
    await settle();
    lease();
    await settle();
    expect(last().unsubscribes).toBe(2);
    expect(last().stopped).toBe(true);
  });

  it('retries a refused connection with a growing wait, then follows the scope', async () => {
    const { hub, state, last } = setup(2);
    hub.setScope(scope());
    hub.acquire();
    await settle();
    expect(hub.getSnapshot().connection).toBe('retrying');
    expect(state.attempts).toBe(1);

    await vi.advanceTimersByTimeAsync(99);
    expect(state.attempts).toBe(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(state.attempts).toBe(2);
    // The second wait is the next in the list (200 ms), not the first again.
    await vi.advanceTimersByTimeAsync(199);
    expect(state.attempts).toBe(2);
    await vi.advanceTimersByTimeAsync(1);

    expect(state.attempts).toBe(3);
    expect(hub.getSnapshot().connection).toBe('connected');
    expect(hub.getSnapshot().connects).toBe(1);
    expect(last().subscribes).toEqual([scope()]);
  });

  it('reconnects after the server closes the connection and sends the scope again', async () => {
    const { hub, links, last } = setup();
    hub.setScope(scope('a'));
    hub.acquire();
    await settle();
    expect(last().subscribes).toHaveLength(1);

    last().close();
    await settle();
    expect(hub.getSnapshot().connection).toBe('retrying');
    expect(hub.getSnapshot().ack).toBeNull();

    await vi.advanceTimersByTimeAsync(100);
    expect(links).toHaveLength(2);
    expect(hub.getSnapshot().connection).toBe('connected');
    expect(hub.getSnapshot().connects).toBe(2);
    expect(last().subscribes).toEqual([scope('a')]);
  });

  it('asks again later when the server could not read the scope', async () => {
    const { hub, state, last } = setup(0, { subscribed: 0, truncated: false, failed: true });
    hub.setScope(scope());
    hub.acquire();
    await settle();
    expect(hub.getSnapshot().ack?.failed).toBe(true);
    expect(last().subscribes).toHaveLength(1);

    state.ack = { subscribed: 3, truncated: true, failed: false };
    await vi.advanceTimersByTimeAsync(1000);
    expect(last().subscribes).toHaveLength(2);
    expect(hub.getSnapshot().ack).toEqual({ subscribed: 3, truncated: true, failed: false });
  });

  it('asks again later when the Subscribe call itself throws', async () => {
    const { hub, state, last } = setup();
    state.subscribeThrows = 1;
    hub.setScope(scope());
    hub.acquire();
    await settle();
    expect(last().subscribes).toHaveLength(1);
    expect(hub.getSnapshot().ack).toBeNull();

    await vi.advanceTimersByTimeAsync(999);
    expect(last().subscribes).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(last().subscribes).toHaveLength(2);
    expect(hub.getSnapshot().ack).toEqual({ subscribed: 2, truncated: false, failed: false });
  });

  it('asks again later when Unsubscribe itself throws, until the server has dropped the scope', async () => {
    const { hub, state, last } = setup();
    hub.setScope(scope());
    hub.acquire();
    await settle();
    state.unsubscribeThrows = 1;

    hub.setScope(null);
    await settle();
    expect(last().unsubscribes).toBe(1);

    await vi.advanceTimersByTimeAsync(1000);
    expect(last().unsubscribes).toBe(2);
    expect(hub.getSnapshot().ack).toBeNull();
    await vi.advanceTimersByTimeAsync(5000);
    expect(last().unsubscribes).toBe(2);
  });

  it('does not ask again on its own when the connection ended while asking (the reconnect sends the scope)', async () => {
    const { hub, state, links, last } = setup();
    state.subscribeThrows = 1;
    hub.setScope(scope());
    hub.acquire();
    await settle();
    last().close();
    await vi.advanceTimersByTimeAsync(100);

    // One ask on the first connection, one on the second; the wait of the first was dropped with the connection.
    expect(links).toHaveLength(2);
    expect(links[0]?.subscribes).toHaveLength(1);
    expect(links[1]?.subscribes).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(5000);
    expect(links[1]?.subscribes).toHaveLength(1);
  });

  describe('the back-off', () => {
    /** The connection ends right after it was accepted, `times` times in a row; returns the waits seen between attempts. */
    async function waitsAfterAccepts(hub: EventsHub, state: { attempts: number }, last: () => Link, times: number) {
      const waits: number[] = [];
      for (let index = 0; index < times; index += 1) {
        const before = state.attempts;
        last().close();
        await settle();
        let waited = 0;
        while (state.attempts === before && waited < 5000) {
          await vi.advanceTimersByTimeAsync(10);
          waited += 10;
        }
        waits.push(waited);
      }
      expect(hub.getSnapshot().connection).toBe('connected');
      return waits;
    }

    it('keeps growing when the server accepts and ends the connection at once', async () => {
      const { hub, state, last } = setup();
      hub.acquire();
      await settle();

      expect(await waitsAfterAccepts(hub, state, last, 3)).toEqual([100, 200, 400]);
    });

    it('starts over after the server answered Subscribe', async () => {
      const { hub, state, last } = setup();
      hub.setScope(scope());
      hub.acquire();
      await settle();

      expect(await waitsAfterAccepts(hub, state, last, 3)).toEqual([100, 100, 100]);
    });

    it('does not start over for the feed status alone (the server sends it right after accepting)', async () => {
      const { hub, state, last } = setup();
      hub.acquire();
      await settle();
      last().close();
      await vi.advanceTimersByTimeAsync(100);
      last().status({ state: 'connected', subscribed: 0, lastTickAt: null });
      last().close();
      await settle();
      const before = state.attempts;

      // The second wait (200 ms), not the first again.
      await vi.advanceTimersByTimeAsync(100);
      expect(state.attempts).toBe(before);
      await vi.advanceTimersByTimeAsync(100);
      expect(state.attempts).toBe(before + 1);
    });

    it('starts over after the connection held for ten seconds', async () => {
      const { hub, state, last } = setup();
      hub.acquire();
      await settle();
      last().close();
      await vi.advanceTimersByTimeAsync(100);
      expect(state.attempts).toBe(2);

      await vi.advanceTimersByTimeAsync(10_000);
      last().close();
      await settle();
      await vi.advanceTimersByTimeAsync(100);
      expect(state.attempts).toBe(3);
    });
  });

  it('passes the prices to the handlers and the status into the state, and nothing after the last holder left', async () => {
    const { hub, last } = setup();
    const received: PricesChanged[] = [];
    hub.onPrices((message) => received.push(message));
    const lease = hub.acquire();
    await settle();

    const message: PricesChanged = {
      asOf: '2026-10-14T05:00:00Z',
      instruments: [{ instrumentId: 'i1', lastPrice: 10, priceAt: '2026-10-14T05:00:00Z', source: 'live' }],
    };
    const link = last();
    link.prices(message);
    link.status({ state: 'connected', subscribed: 1, lastTickAt: null });
    expect(received).toEqual([message]);
    expect(hub.getSnapshot().status).toEqual({ state: 'connected', subscribed: 1, lastTickAt: null });

    lease();
    await settle();
    link.prices(message);
    expect(received).toHaveLength(1);
    expect(hub.getSnapshot().status).toBeNull();
  });

  it('removes a price handler', async () => {
    const { hub, last } = setup();
    const received: PricesChanged[] = [];
    const off = hub.onPrices((message) => received.push(message));
    hub.acquire();
    await settle();
    off();
    last().prices({ asOf: '2026-10-14T05:00:00Z', instruments: [] });
    expect(received).toEqual([]);
  });
});
