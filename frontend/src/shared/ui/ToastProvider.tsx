import { Alert, Snackbar } from '@mui/material';
import { useMemo, useState, type ReactNode } from 'react';
import { ToastContext, type ToastApi, type ToastSeverity } from './toastContext';

interface Toast {
  id: number;
  message: string;
  severity: ToastSeverity;
}

/** Owns one snackbar; descendants call `useToast().notify(...)`. Announced politely to screen readers. */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toast, setToast] = useState<Toast | null>(null);
  const api = useMemo<ToastApi>(
    () => ({
      notify: (message, severity = 'success') => {
        setToast((previous) => ({ id: (previous?.id ?? 0) + 1, message, severity }));
      },
    }),
    [],
  );

  return (
    <ToastContext value={api}>
      {children}
      <Snackbar
        key={toast?.id}
        open={toast !== null}
        autoHideDuration={5000}
        onClose={(_event, reason) => {
          if (reason !== 'clickaway') {
            setToast(null);
          }
        }}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert
          severity={toast?.severity ?? 'success'}
          variant="filled"
          onClose={() => {
            setToast(null);
          }}
        >
          {toast?.message}
        </Alert>
      </Snackbar>
    </ToastContext>
  );
}
