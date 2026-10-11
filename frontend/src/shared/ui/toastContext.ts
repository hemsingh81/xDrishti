import { createContext, useContext } from 'react';

export type ToastSeverity = 'success' | 'info' | 'error';

export interface ToastApi {
  notify: (message: string, severity?: ToastSeverity) => void;
}

export const ToastContext = createContext<ToastApi>({
  // No provider: nothing to show the message in, so the call is a no-op.
  notify: () => undefined,
});

/** Shows a short, polite confirmation ("Saved") in the nearest ToastProvider. */
export function useToast(): ToastApi {
  return useContext(ToastContext);
}
