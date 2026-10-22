import { createSignalrTransport } from './signalrTransport';

interface Calls {
  url: string;
  options: unknown;
  invoked: unknown[][];
  handlers: Map<string, (message: unknown) => void>;
  closed: (() => void) | undefined;
}

const calls = vi.hoisted((): Calls => ({
  url: '',
  options: undefined,
  invoked: [],
  handlers: new Map(),
  closed: undefined,
}));

vi.mock('@microsoft/signalr', () => {
  class Builder {
    withUrl(url: string, options: unknown) {
      calls.url = url;
      calls.options = options;
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return {
        start: () => Promise.resolve(),
        stop: () => Promise.resolve(),
        invoke: (...args: unknown[]) => {
          calls.invoked.push(args);
          return Promise.resolve({ subscribed: 1, truncated: false, failed: false });
        },
        on: (event: string, handler: (message: unknown) => void) => {
          calls.handlers.set(event, handler);
        },
        onclose: (handler: () => void) => {
          calls.closed = handler;
        },
      };
    }
  }
  return { HubConnectionBuilder: Builder, HttpTransportType: { WebSockets: 1 }, LogLevel: { None: 6 } };
});

describe('the SignalR transport', () => {
  it('connects to the hub of this origin over WebSockets only, without negotiation', () => {
    createSignalrTransport();

    expect(calls.url).toBe('/hubs/events');
    expect(calls.options).toEqual({ skipNegotiation: true, transport: 1 });
  });

  it('calls the methods and listens to the events of the generated contract', async () => {
    const transport = createSignalrTransport();
    const prices = vi.fn();
    const status = vi.fn();
    const closed = vi.fn();
    transport.onPricesChanged(prices);
    transport.onFeedStatus(status);
    const labProgress = vi.fn();
    transport.onLabRunProgress(labProgress);
    transport.onLabRunFinished(vi.fn());
    transport.onClose(closed);

    await expect(transport.subscribe({ accountId: null, bucketId: null, holdingIds: ['h1'] })).resolves.toMatchObject({
      subscribed: 1,
    });
    await transport.unsubscribe();
    expect(calls.invoked).toEqual([
      ['Subscribe', { accountId: null, bucketId: null, holdingIds: ['h1'] }],
      ['Unsubscribe'],
    ]);

    calls.handlers.get('pricesChanged')?.({ asOf: 'x', instruments: [] });
    calls.handlers.get('feedStatus')?.({ state: 'connected', subscribed: 0, lastTickAt: null });
    calls.handlers.get('labRunProgress')?.({});
    expect(labProgress).toHaveBeenCalledOnce();
    calls.closed?.();
    expect(prices).toHaveBeenCalledOnce();
    expect(status).toHaveBeenCalledOnce();
    expect(closed).toHaveBeenCalledOnce();
  });
});
