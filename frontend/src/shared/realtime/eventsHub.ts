import type {
  LabRunFinished,
  LabRunProgress,
  LiveFeedStatus,
  LiveScope,
  PricesChanged,
  SubscribeAck,
} from './hubContract.generated';

/**
 * What the hub client needs from a connection to `/hubs/events`, in the terms of the generated contract. The real one is a
 * SignalR connection (`signalrTransport.ts`, loaded on demand); a test brings a fake (`src/test/hubFake.ts`).
 */
export interface HubTransport {
  /** Opens the connection; rejects when it cannot (feed off, proxy, network, no session). */
  start(): Promise<void>;
  stop(): Promise<void>;
  subscribe(scope: LiveScope): Promise<SubscribeAck>;
  unsubscribe(): Promise<void>;
  /** The handlers of the server's events; set before `start`. */
  onPricesChanged(handler: (message: PricesChanged) => void): void;
  onFeedStatus(handler: (message: LiveFeedStatus) => void): void;
  onLabRunProgress(handler: (message: LabRunProgress) => void): void;
  onLabRunFinished(handler: (message: LabRunFinished) => void): void;
  /** Called once when a started connection ends (the server closed it, the network was lost, the session ended). */
  onClose(handler: () => void): void;
}

/** Creates a transport; it may load its library on the way (the real one does). Called again for every connection attempt. */
export type HubTransportFactory = () => Promise<HubTransport>;

export type HubConnection = 'idle' | 'connecting' | 'connected' | 'retrying';

export interface HubSnapshot {
  connection: HubConnection;
  /** The answer to the last `Subscribe` of this connection; null before it, after a disconnect and without a scope. */
  ack: SubscribeAck | null;
  /** When the connection first got a good answer (ms); null with the answer. */
  ackAt: number | null;
  /** The feed's state as the hub last pushed it; null until the first message of a connection. */
  status: LiveFeedStatus | null;
  /** How many times a connection was established (a reconnect makes it grow). */
  connects: number;
}

export interface EventsHubOptions {
  /** Wait before the next attempt after a failure (ms), growing; the last one repeats. */
  retryDelaysMs: readonly number[];
  /** A number in [0, 1): spreads the waits so that tabs do not retry together. */
  random: () => number;
  /** Wait before asking again after the server could not read the scope (ms). */
  resubscribeMs: number;
  /**
   * How long a connection must hold, without a good `Subscribe` answer, before the back-off starts over (ms). A
   * connection the server accepts and then ends at once must not reset it, or the retries would run at the first wait for ever.
   */
  stableMs?: number;
}

export const defaultHubOptions: EventsHubOptions = {
  retryDelaysMs: [1000, 2000, 5000, 10_000, 20_000, 30_000],
  random: Math.random,
  resubscribeMs: 15_000,
  stableMs: 10_000,
};

const idle: HubSnapshot = { connection: 'idle', ack: null, ackAt: null, status: null, connects: 0 };

const scopeKey = (scope: LiveScope): string => JSON.stringify(scope);

/**
 * One connection to the hub per tab, shared by every screen that asks for it (a reference count: the first `acquire` opens
 * it, the last release closes it). A lost connection is reopened with a growing, spread wait; whatever scope the screens
 * asked for last is sent again each time. Messages go to the handlers added with `onPrices`; the state is observable with
 * `subscribe` / `getSnapshot` (for `useSyncExternalStore`).
 */
export class EventsHub {
  private snapshot: HubSnapshot = idle;
  private readonly stateListeners = new Set<() => void>();
  private readonly priceHandlers = new Set<(message: PricesChanged) => void>();
  private readonly labProgressHandlers = new Set<(message: LabRunProgress) => void>();
  private readonly labFinishedHandlers = new Set<(message: LabRunFinished) => void>();
  private leases = 0;
  /** Grows with every open and close, so a late answer of an earlier connection is recognised and dropped. */
  private generation = 0;
  private transport: HubTransport | undefined;
  private attempt = 0;
  private retryTimer: ReturnType<typeof setTimeout> | undefined;
  private resubscribeTimer: ReturnType<typeof setTimeout> | undefined;
  private stableTimer: ReturnType<typeof setTimeout> | undefined;
  private wanted: LiveScope | null = null;
  /** What the server follows for this connection: the key of the scope sent, or null. */
  private sent: string | null = null;
  private syncing: Promise<void> = Promise.resolve();

  constructor(
    private readonly factory: HubTransportFactory,
    private readonly options: EventsHubOptions = defaultHubOptions,
  ) {}

  readonly getSnapshot = (): HubSnapshot => this.snapshot;

  readonly subscribe = (listener: () => void): (() => void) => {
    this.stateListeners.add(listener);
    return () => {
      this.stateListeners.delete(listener);
    };
  };

  /** Adds a handler of `pricesChanged`; returns the function that removes it. */
  readonly onPrices = (handler: (message: PricesChanged) => void): (() => void) => {
    this.priceHandlers.add(handler);
    return () => {
      this.priceHandlers.delete(handler);
    };
  };

  /** Adds a handler of `labRunProgress` / `labRunFinished`; returns the function that removes it. */
  readonly onLabProgress = (handler: (message: LabRunProgress) => void): (() => void) => {
    this.labProgressHandlers.add(handler);
    return () => this.labProgressHandlers.delete(handler) && undefined;
  };

  readonly onLabFinished = (handler: (message: LabRunFinished) => void): (() => void) => {
    this.labFinishedHandlers.add(handler);
    return () => this.labFinishedHandlers.delete(handler) && undefined;
  };

  /** Asks for the connection; the returned function gives it back (call it once). */
  acquire(): () => void {
    this.leases += 1;
    if (this.leases === 1) this.open();
    let released = false;
    return () => {
      if (released) return;
      released = true;
      this.leases -= 1;
      if (this.leases === 0) this.close();
    };
  }

  /**
   * What the screen wants to follow (null: nothing). Sent as soon as the connection is up, and sent again after every
   * reconnect; a scope equal to the one the server already has is not sent twice.
   */
  setScope(scope: LiveScope | null): void {
    this.wanted = scope;
    this.sync();
  }

  private set(patch: Partial<HubSnapshot>): void {
    this.snapshot = { ...this.snapshot, ...patch };
    for (const listener of this.stateListeners) listener();
  }

  private open(): void {
    this.generation += 1;
    this.attempt = 0;
    void this.connect(this.generation);
  }

  private close(): void {
    this.generation += 1;
    clearTimeout(this.retryTimer);
    clearTimeout(this.resubscribeTimer);
    clearTimeout(this.stableTimer);
    const transport = this.transport;
    const following = this.sent !== null;
    this.transport = undefined;
    this.sent = null;
    this.snapshot = { ...idle, connects: this.snapshot.connects };
    for (const listener of this.stateListeners) listener();
    if (transport) void leave(transport, following);
  }

  private async connect(generation: number): Promise<void> {
    this.set({ connection: 'connecting' });
    let transport: HubTransport | undefined;
    try {
      transport = await this.factory();
      transport.onPricesChanged((message) => {
        if (generation !== this.generation) return;
        for (const handler of this.priceHandlers) handler(message);
      });
      transport.onLabRunProgress((message) => {
        if (generation !== this.generation) return;
        for (const handler of this.labProgressHandlers) handler(message);
      });
      transport.onLabRunFinished((message) => {
        if (generation !== this.generation) return;
        for (const handler of this.labFinishedHandlers) handler(message);
      });
      transport.onFeedStatus((status) => {
        if (generation === this.generation) this.set({ status });
      });
      const mine = transport;
      transport.onClose(() => {
        if (generation === this.generation && this.transport === mine) this.lost();
      });
      await transport.start();
    } catch {
      if (transport) void transport.stop().catch(() => undefined);
      if (generation === this.generation) this.retry(generation);
      return;
    }
    if (generation !== this.generation) {
      void transport.stop().catch(() => undefined);
      return;
    }
    this.transport = transport;
    this.sent = null;
    // The back-off starts over only once the connection has shown it works (see `healthy`), not at the accept; the server's
    // first `feedStatus` comes right after the accept and says nothing about that.
    clearTimeout(this.stableTimer);
    this.stableTimer = setTimeout(
      () => {
        this.healthy(generation);
      },
      this.options.stableMs ?? defaultHubOptions.stableMs ?? 10_000,
    );
    this.set({ connection: 'connected', ack: null, ackAt: null, connects: this.snapshot.connects + 1 });
    this.sync();
  }

  /** The server answered (a good `Subscribe` ack) or the connection held: the next failure waits the first wait again. */
  private healthy(generation: number): void {
    if (generation !== this.generation) return;
    this.attempt = 0;
    clearTimeout(this.stableTimer);
  }

  /** The connection ended without being asked to: forget what the server followed and try again. */
  private lost(): void {
    this.transport = undefined;
    this.sent = null;
    clearTimeout(this.resubscribeTimer);
    clearTimeout(this.stableTimer);
    this.set({ ack: null, ackAt: null, status: null });
    this.retry(this.generation);
  }

  private retry(generation: number): void {
    this.set({ connection: 'retrying' });
    const { retryDelaysMs, random } = this.options;
    const base = retryDelaysMs[Math.min(this.attempt, retryDelaysMs.length - 1)] ?? 30_000;
    this.attempt += 1;
    // 80 % to 120 % of the wait.
    const wait = Math.round(base * (0.8 + random() * 0.4));
    clearTimeout(this.retryTimer);
    this.retryTimer = setTimeout(() => {
      if (generation === this.generation) void this.connect(generation);
    }, wait);
  }

  /** Brings the server's subscription in line with the wanted scope; one call at a time, in order. */
  private sync(): void {
    const generation = this.generation;
    this.syncing = this.syncing.then(() => this.send(generation)).catch(() => undefined);
  }

  private async send(generation: number): Promise<void> {
    const transport = this.transport;
    if (!transport || generation !== this.generation) return;
    const wanted = this.wanted;
    const key = wanted === null ? null : scopeKey(wanted);
    if (key === this.sent) return;
    try {
      if (wanted === null) {
        await transport.unsubscribe();
        this.sent = null;
        if (generation === this.generation) this.set({ ack: null, ackAt: null });
        return;
      }
      const ack = await transport.subscribe(wanted);
      if (generation !== this.generation) return;
      this.sent = ack.failed ? null : key;
      this.set({ ack, ackAt: ack.failed ? null : (this.snapshot.ackAt ?? Date.now()) });
      if (ack.failed) this.askAgainLater(generation);
      else this.healthy(generation);
    } catch {
      // The connection may have broken while asking: `onClose` then brings the reconnect (which clears this wait and sends
      // the scope again). If it did not, the call itself failed, and the scope is asked for again later, as after a failed ack.
      // The same for an `Unsubscribe` that failed while the server may still follow the old scope.
      if (generation === this.generation && (wanted !== null || this.sent !== null)) this.askAgainLater(generation);
    }
  }

  private askAgainLater(generation: number): void {
    clearTimeout(this.resubscribeTimer);
    this.resubscribeTimer = setTimeout(() => {
      if (generation === this.generation) this.sync();
    }, this.options.resubscribeMs);
  }
}

/** How long a connection that is being closed waits for the server to acknowledge `Unsubscribe`. */
const leaveWaitMs = 1000;

/** Tells the server the screen is leaving (when it was following something), then closes the connection; never throws. */
async function leave(transport: HubTransport, following: boolean): Promise<void> {
  try {
    if (following) {
      await Promise.race([transport.unsubscribe(), new Promise((resolve) => setTimeout(resolve, leaveWaitMs))]);
    }
  } catch {
    // The connection may already be gone; closing it is all that is left.
  }
  await transport.stop().catch(() => undefined);
}

/** The library is loaded here, when the first screen asks for a connection (it ships with the Portfolio chunk). */
async function loadSignalrTransport(): Promise<HubTransport> {
  const { createSignalrTransport } = await import('./signalrTransport');
  return createSignalrTransport();
}

let shared: EventsHub | undefined;
let sharedFactory: HubTransportFactory | undefined;
let sharedOptions: EventsHubOptions | undefined;

/** The tab's hub client, created on first use. */
export function getEventsHub(): EventsHub {
  shared ??= new EventsHub(sharedFactory ?? loadSignalrTransport, sharedOptions);
  return shared;
}

/**
 * Replaces the way connections are made and drops the current client (tests: a fake hub; the app never calls it). Called
 * without arguments it restores the real transport.
 */
export function setHubTransport(factory?: HubTransportFactory, options?: EventsHubOptions): void {
  shared = undefined;
  sharedFactory = factory;
  sharedOptions = options;
}
