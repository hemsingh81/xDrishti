import { useEffect, useRef } from 'react';
import { useToast, type ToastAction, type ToastSeverity } from '@/shared/ui/toastContext';
import {
  attentionKinds,
  type AppNotification,
  type NotificationAction,
  type NotificationSeverity,
} from '../lib/deriveNotifications';
import { browserStorage, loadPoppedIds, markPopped, stateKey, type ReadStorage } from '../lib/readState';
import { useForgetResolved } from './useForgetResolved';
import { useNotifications } from './useNotifications';
import { useReadState } from './useReadState';
import { useRunNotificationAction } from './useRunNotificationAction';

const toastSeverity: Record<NotificationSeverity, ToastSeverity> = { error: 'error', warn: 'warning', info: 'info' };
const toastId = (key: string) => `attention:${key}`;

/** What the pop-up offers: the notice's own buttons, or Open when it only names a place. */
const actionsOf = (n: AppNotification): NotificationAction[] =>
  n.actions.length > 0 ? n.actions : [{ label: 'Open', kind: 'route', target: n.href }];

/**
 * Pops up the items that need the owner (ADR 0027, P2-16 criterion 14), once each. The first time an unread item appears while
 * the app is open a toast shows its title, text and buttons; an error stays until it is closed, acted on or resolved. A button
 * marks the item read, then goes to the page or runs the command. What popped up is remembered in this browser (apart from
 * "read"), so a reload does not show it again. When the app opens, only the most urgent unread item pops up (the list is in
 * that order); the others wait in the bell and are not shown one by one on later reloads. A toast closes by itself when its
 * item is resolved, gone or read in the bell.
 *
 * Mount it once, below the ToastProvider and the router. `storage` is injectable for tests; null disables persistence.
 */
export function useAttentionPopups(storage?: ReadStorage | null): void {
  const { notifications, ready } = useNotifications();
  const { readIds, markItemsRead, forget } = useReadState(storage);
  useForgetResolved(notifications, forget);
  const { notify, dismiss } = useToast();
  const { run } = useRunNotificationAction();
  const resolved = storage === undefined ? browserStorage() : storage;
  const opened = useRef(false);
  /** Keys (`stateKey`: id plus `since`) that have had their pop-up in this page load (so a poll never shows one twice). */
  const handled = useRef(new Set<string>());
  /** Keys whose toast may still be open. */
  const live = useRef(new Set<string>());

  useEffect(() => {
    if (!ready) return;

    // A toast whose item is resolved, gone or read has nothing left to say.
    const current = new Map(notifications.map((n) => [stateKey(n), n]));
    for (const key of [...live.current]) {
      const n = current.get(key);
      if (!n || n.resolved || readIds.has(key)) {
        dismiss(toastId(key));
        live.current.delete(key);
      }
    }

    // A resolved item is over: if the same situation comes back it is a new one and may pop up again.
    for (const n of notifications) if (n.resolved) handled.current.delete(stateKey(n));

    const popped = loadPoppedIds(resolved);
    const fresh = notifications.filter(
      (n) =>
        attentionKinds.has(n.kind) &&
        !n.resolved &&
        !readIds.has(stateKey(n)) &&
        !popped.has(stateKey(n)) &&
        !handled.current.has(stateKey(n)),
    );
    if (fresh.length === 0) {
      opened.current = true;
      return;
    }
    const first = !opened.current;
    opened.current = true;
    // On opening only the most urgent pops up; the rest are remembered as seen, so that they wait in the bell.
    const showing = first ? fresh.slice(0, 1) : fresh;
    for (const n of fresh) handled.current.add(stateKey(n));
    markPopped(resolved, fresh.map(stateKey));

    for (const n of showing) {
      const actions: ToastAction[] = actionsOf(n).map((action) => ({
        label: action.label,
        onClick: () => {
          markItemsRead([n]);
          void run(action, `${n.id}|${action.label}`);
        },
      }));
      live.current.add(stateKey(n));
      notify(n.text, toastSeverity[n.severity], {
        id: toastId(stateKey(n)),
        title: n.title ?? undefined,
        actions,
        persistent: n.severity === 'error',
      });
    }
  }, [ready, notifications, readIds, resolved, notify, dismiss, run, markItemsRead]);
}
