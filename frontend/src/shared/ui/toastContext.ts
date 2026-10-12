import { createContext, useContext } from 'react';

export type ToastSeverity = 'success' | 'info' | 'warning' | 'error';

/** A button in the toast (e.g. "Undo"). The toast stays longer, and closes when the button was pressed. */
export interface ToastAction {
  label: string;
  onClick: () => void;
}

export interface ToastApi {
  notify: (message: string, severity?: ToastSeverity, action?: ToastAction) => void;
}

export const ToastContext = createContext<ToastApi>({
  // No provider: nothing to show the message in, so the call is a no-op.
  notify: () => undefined,
});

/** Shows a short, polite confirmation ("Saved") in the nearest ToastProvider. */
export function useToast(): ToastApi {
  return useContext(ToastContext);
}
