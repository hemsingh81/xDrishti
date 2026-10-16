// Public API of the notifications feature. Other code imports only from here.
import { lazy } from 'react';

export type { AppNotification } from './lib/deriveNotifications';

/** The top-bar bell. Loaded on demand so the drawer and the derivation stay out of the first load; render in <Suspense>. */
export const NotificationBell = lazy(async () => ({
  default: (await import('./components/NotificationBell')).NotificationBell,
}));

/** Pops up the items that need the owner, once each. Mount once in the shell, inside the ToastProvider and the router; render in <Suspense>. */
export const AttentionPopups = lazy(async () => ({
  default: (await import('./components/AttentionPopups')).AttentionPopups,
}));
