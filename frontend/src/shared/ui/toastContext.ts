import { createContext, useContext } from 'react';

export type ToastSeverity = 'success' | 'info' | 'warning' | 'error';

/** A button in the toast (e.g. "Undo"). The toast closes when the button was pressed. */
export interface ToastAction {
  label: string;
  onClick: () => void;
}

/** The most buttons a toast carries; more would not fit on a phone and would ask the owner to choose too much. */
export const maxToastActions = 2;

/** Everything beyond the message and severity, all optional. */
export interface ToastOptions {
  /** A short bold first line ("Reconnect Dhan"); the message then reads as its explanation. */
  title?: string;
  /**
   * An undoable action: a button, plus Ctrl/Cmd+Z for as long as the toast is open. The toast stays 10 s. Shown first;
   * together with `actions` at most two buttons are shown.
   */
  action?: ToastAction;
  /** Up to two buttons (more are ignored). Unlike `action` they do not take Ctrl/Cmd+Z. The toast stays 10 s. */
  actions?: readonly ToastAction[];
  /** Stays until it is closed, one of its buttons is pressed, it is replaced or `dismiss`ed; no countdown. */
  persistent?: boolean;
  /** Names the toast: notifying again with the same id replaces it in place (and restarts its time) instead of adding one. */
  id?: string;
  /** How long a timed toast stays, in ms (default 5 s, or 10 s with buttons). Ignored when `persistent`. */
  durationMs?: number;
}

export interface ToastApi {
  /**
   * Shows a toast. The third argument is a single button (the original form, e.g. Undo) or the options. Up to three
   * toasts show at once, newest at the bottom; a fourth makes the oldest leave (a timed one before a persistent one).
   */
  notify: (message: string, severity?: ToastSeverity, actionOrOptions?: ToastAction | ToastOptions) => void;
  /** Closes the toast with this id, if it is still open. */
  dismiss: (id: string) => void;
}

export const ToastContext = createContext<ToastApi>({
  // No provider: nothing to show the message in, so the calls are no-ops.
  notify: () => undefined,
  dismiss: () => undefined,
});

/** Shows a short confirmation ("Saved") or a notice with buttons in the nearest ToastProvider. */
export function useToast(): ToastApi {
  return useContext(ToastContext);
}
