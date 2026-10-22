import { act } from '@testing-library/react';
import {
  setHubTransport,
  type HubTransport,
  type InstrumentPrice,
  type LabRunFinished,
  type LabRunProgress,
  type LiveFeedStatus,
  type LiveScope,
  type PricesChanged,
  type SubscribeAck,
} from '@/shared/realtime';

/** Short waits, so a test that lets the hub fail and come back does not wait for the real back-off. */
export const fastHubOptions = { retryDelaysMs: [20, 40, 80], random: () => 0.5, resubscribeMs: 50 };

export type HubBehaviour = 'connect' | 'refuse' | 'hang';

/** A made-up instrument id (the hub carries instrument ids, the holdings list has them as `instrumentId`). */
export const instrumentOf = (n: number): string => `00000000-0000-4000-8000-0000000b${String(n).padStart(4, '0')}`;

export const priceOf = (
  n: number,
  lastPrice: number,
  priceAt: string,
  source: InstrumentPrice['source'] = 'live',
): InstrumentPrice => ({ instrumentId: instrumentOf(n), lastPrice, priceAt, source });

export const statusOf = (overrides: Partial<LiveFeedStatus> = {}): LiveFeedStatus => ({
  state: 'connected',
  subscribed: 3,
  lastTickAt: null,
  ...overrides,
});

/**
 * A stand-in for the hub: tests decide whether a connection succeeds, what the server pushes and when it hangs up, and read
 * back what the screen subscribed to. Install with `fake.install()`; the default of every test (setup.ts) is a hub that never
 * answers, which is the same as no hub at all.
 */
export function createHubFake(behaviour: HubBehaviour = 'connect') {
  let mode = behaviour;
  let pricesHandler: ((message: PricesChanged) => void) | undefined;
  let statusHandler: ((message: LiveFeedStatus) => void) | undefined;
  let labProgressHandler: ((message: LabRunProgress) => void) | undefined;
  let labFinishedHandler: ((message: LabRunFinished) => void) | undefined;
  let closeHandler: (() => void) | undefined;
  let alive = false;

  const fake = {
    /** Every scope the screen sent with `Subscribe`, in order. */
    subscribes: [] as LiveScope[],
    unsubscribes: 0,
    /** How many connections were attempted (successful or not). */
    attempts: 0,
    /** What `Subscribe` answers. */
    ack: { subscribed: 3, truncated: false, failed: false } as SubscribeAck,
    /** Sent to the screen when a connection is established, as the server does. */
    greeting: statusOf() as LiveFeedStatus | null,
    set behaviour(next: HubBehaviour) {
      mode = next;
    },
    get alive() {
      return alive;
    },
    get lastScope(): LiveScope | undefined {
      return fake.subscribes.at(-1);
    },
    factory: async (): Promise<HubTransport> => {
      fake.attempts += 1;
      await Promise.resolve();
      return {
        start: async () => {
          if (mode === 'hang') return new Promise<void>(() => undefined);
          if (mode === 'refuse') throw new Error('refused');
          alive = true;
          // The server's first message on a connection is the status.
          if (fake.greeting) {
            const greeting = fake.greeting;
            queueMicrotask(() => {
              act(() => {
                statusHandler?.(greeting);
              });
            });
          }
        },
        stop: () => {
          alive = false;
          return Promise.resolve();
        },
        subscribe: (scope) => {
          fake.subscribes.push(scope);
          return Promise.resolve(fake.ack);
        },
        unsubscribe: () => {
          fake.unsubscribes += 1;
          return Promise.resolve();
        },
        onPricesChanged: (handler) => {
          pricesHandler = handler;
        },
        onFeedStatus: (handler) => {
          statusHandler = handler;
        },
        onLabRunProgress: (handler) => {
          labProgressHandler = handler;
        },
        onLabRunFinished: (handler) => {
          labFinishedHandler = handler;
        },
        onClose: (handler) => {
          closeHandler = handler;
        },
      };
    },
    install: () => {
      setHubTransport(fake.factory, fastHubOptions);
    },
    /** The server pushes prices. */
    push: (instruments: InstrumentPrice[], asOf = new Date().toISOString()) => {
      act(() => {
        pricesHandler?.({ asOf, instruments });
      });
    },
    /** The server pushes the feed's state. */
    status: (status: LiveFeedStatus) => {
      act(() => {
        statusHandler?.(status);
      });
    },
    /** The server reports a Lab run's progress. */
    labProgress: (message: LabRunProgress) => {
      act(() => {
        labProgressHandler?.(message);
      });
    },
    /** The server reports a Lab run's end. */
    labFinished: (message: LabRunFinished) => {
      act(() => {
        labFinishedHandler?.(message);
      });
    },
    /** The server (or the network) ends the connection. */
    drop: () => {
      alive = false;
      act(() => {
        closeHandler?.();
      });
    },
  };
  return fake;
}

export type HubFake = ReturnType<typeof createHubFake>;
