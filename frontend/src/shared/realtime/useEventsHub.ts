import { useEffect, useSyncExternalStore } from 'react';
import { getEventsHub, type EventsHub, type HubSnapshot } from './eventsHub';

/**
 * Holds a connection to the hub for as long as `enabled` is true (the component is mounted and wants it); the tab's single
 * connection is opened by the first holder and closed when the last one lets go. Returns the client and its state.
 */
export function useEventsHub(enabled: boolean): { hub: EventsHub; state: HubSnapshot } {
  const hub = getEventsHub();
  const state = useSyncExternalStore(hub.subscribe, hub.getSnapshot);
  useEffect(() => {
    if (!enabled) return;
    return hub.acquire();
  }, [hub, enabled]);
  return { hub, state };
}
