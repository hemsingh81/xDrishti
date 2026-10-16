import type { PricesChanged } from '@/shared/realtime';

/** The newest price the hub has pushed for an instrument. `at` is the price time (ms), `atIso` the same as the API writes it. */
export interface LiveTick {
  price: number;
  at: number;
  atIso: string;
  /** `live` (a tick of the feed), `quote` or `sync`. */
  source: string;
}

export type LiveTicks = ReadonlyMap<string, LiveTick>;

export interface LiveTickState {
  ticks: LiveTicks;
  /** Server time minus browser time at the last message (ms): the ages are measured on the server's clock. */
  skewMs: number;
}

export const noTicks: LiveTickState = { ticks: new Map(), skewMs: 0 };

/**
 * Adds a `pricesChanged` message. An older price time for an instrument is ignored (the server keeps the order, but a
 * client must not rely on it); the same time with another price replaces (ticks have whole-second times, ADR 0024). Returns
 * the same state when nothing in the message was new.
 */
export function applyPrices(state: LiveTickState, message: PricesChanged, receivedAt: number): LiveTickState {
  let next: Map<string, LiveTick> | undefined;
  for (const item of message.instruments) {
    if (!(item.lastPrice > 0)) continue;
    const at = Date.parse(item.priceAt);
    if (Number.isNaN(at)) continue;
    const known = (next ?? state.ticks).get(item.instrumentId);
    if (
      known &&
      (at < known.at || (at === known.at && item.lastPrice === known.price && item.source === known.source))
    ) {
      continue;
    }
    next ??= new Map(state.ticks);
    next.set(item.instrumentId, { price: item.lastPrice, at, atIso: item.priceAt, source: item.source });
  }
  const asOf = Date.parse(message.asOf);
  const skewMs = Number.isNaN(asOf) ? state.skewMs : asOf - receivedAt;
  if (!next) return skewMs === state.skewMs ? state : { ticks: state.ticks, skewMs };
  return { ticks: next, skewMs };
}

/** The price time (ms) of the newest live tick among the instruments; null when none of them has had one. */
export function newestLiveTick(ticks: LiveTicks, instrumentIds: Iterable<string>): number | null {
  let newest: number | null = null;
  for (const id of instrumentIds) {
    const tick = ticks.get(id);
    if (tick?.source === 'live' && (newest === null || tick.at > newest)) newest = tick.at;
  }
  return newest;
}

/** A store for `useSyncExternalStore`: the ticks of this screen, replaced (never changed in place) when something is new. */
export class LiveTickStore {
  private state: LiveTickState = noTicks;
  private readonly listeners = new Set<() => void>();

  readonly getSnapshot = (): LiveTickState => this.state;

  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  apply(message: PricesChanged, receivedAt: number): void {
    const next = applyPrices(this.state, message, receivedAt);
    if (next === this.state) return;
    this.state = next;
    for (const listener of this.listeners) listener();
  }
}
