import type { HubTransport } from './eventsHub';

/** Adds a handler to a set; returns the function that removes it. */
export function register<T>(handlers: Set<T>, handler: T): () => void {
  handlers.add(handler);
  return () => {
    handlers.delete(handler);
  };
}

/** How long a connection that is being closed waits for the server to acknowledge `Unsubscribe`. */
const leaveWaitMs = 1000;

/** Tells the server the screen is leaving (when it was following something), then closes the connection; never throws. */
export async function leave(transport: HubTransport, following: boolean): Promise<void> {
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
export async function loadSignalrTransport(): Promise<HubTransport> {
  const { createSignalrTransport } = await import('./signalrTransport');
  return createSignalrTransport();
}
