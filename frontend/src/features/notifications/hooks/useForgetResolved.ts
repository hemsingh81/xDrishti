import { useEffect } from 'react';
import type { AppNotification } from '../lib/deriveNotifications';

/**
 * Drops the stored read and popped state of every item that is resolved, so a later occurrence of the same situation (the
 * server re-raises the id with a new `since`) is unread and pops up again. Does nothing when there is nothing stored for them.
 */
export function useForgetResolved(
  notifications: readonly AppNotification[],
  forget: (ids: readonly string[]) => void,
): void {
  useEffect(() => {
    forget(notifications.filter((n) => n.resolved).map((n) => n.id));
  }, [notifications, forget]);
}
