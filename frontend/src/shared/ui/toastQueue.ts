import type { ToastAction, ToastOptions, ToastSeverity } from './toastContext';
import { maxToastActions } from './toastContext';

/** How long a toast stays: plain 5 s; one with a button gives time to read it and decide, 10 s. */
export const plainDurationMs = 5000;
export const actionDurationMs = 10_000;
/** At most this many toasts show at once. */
export const maxVisibleToasts = 3;

export interface ToastItem {
  id: string;
  /** Bumps whenever the same id is notified again, to restart the time and the countdown. */
  revision: number;
  message: string;
  severity: ToastSeverity;
  title?: string;
  /** The buttons shown, at most two. */
  actions: readonly ToastAction[];
  /** The button that also answers Ctrl/Cmd+Z (always `actions[0]`). */
  undo?: ToastAction;
  /** Time on screen in ms; `null` for a persistent toast. */
  durationMs: number | null;
  /** The keyboard started the action behind it, so focus moves to its undo button. */
  focusAction: boolean;
  /** Leaving: kept for the exit animation, then removed; invisible to assistive technology meanwhile. */
  closing: boolean;
}

export function buildToast(
  id: string,
  revision: number,
  message: string,
  severity: ToastSeverity,
  options: ToastOptions,
  focusAction: boolean,
): ToastItem {
  const undo = options.action;
  const actions = [...(undo ? [undo] : []), ...(options.actions ?? [])].slice(0, maxToastActions);
  const persistent = options.persistent === true;
  const fallback = actions.length > 0 ? actionDurationMs : plainDurationMs;
  return {
    id,
    revision,
    message,
    severity,
    title: options.title,
    actions,
    undo,
    durationMs: persistent ? null : (options.durationMs ?? fallback),
    focusAction: focusAction && undo !== undefined,
    closing: false,
  };
}

/** Marks the toast as leaving (the card removes it once its exit animation is over). */
export const closeToast = (toasts: readonly ToastItem[], id: string): ToastItem[] =>
  toasts.map((toast) => (toast.id === id && !toast.closing ? { ...toast, closing: true } : toast));

export const openToasts = (toasts: readonly ToastItem[]): ToastItem[] => toasts.filter((toast) => !toast.closing);

/**
 * Adds a toast at the bottom, or replaces the one with the same id where it stands. When more than three are open the
 * oldest leaves: a timed one first (a persistent notice keeps its place while there is something less important to
 * drop), and the oldest persistent one only if all are persistent (it is also in the bell).
 */
export function pushToast(toasts: readonly ToastItem[], next: ToastItem): ToastItem[] {
  const index = toasts.findIndex((toast) => toast.id === next.id);
  let result = index >= 0 ? toasts.map((toast, i) => (i === index ? next : toast)) : [...toasts, next];
  for (let open = openToasts(result); open.length > maxVisibleToasts; open = openToasts(result)) {
    const leaving = open.find((toast) => toast.durationMs !== null && toast.id !== next.id) ?? open[0];
    if (!leaving) break;
    result = closeToast(result, leaving.id);
  }
  return result;
}
