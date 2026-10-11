import { Alert, Snackbar } from '@mui/material';
import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import InfoIcon from '@mui/icons-material/InfoOutlined';
import { useMemo, useState, type ReactNode } from 'react';
import type { IconHueName } from '@/theme';
import { IconChip } from './IconChip';
import { ToastContext, type ToastApi, type ToastSeverity } from './toastContext';

interface Toast {
  id: number;
  message: string;
  severity: ToastSeverity;
}

const statusChip = {
  success: { icon: CheckCircleIcon, hue: 'green' },
  info: { icon: InfoIcon, hue: 'indigo' },
  error: { icon: ErrorIcon, hue: 'rose' },
} as const satisfies Record<ToastSeverity, { icon: typeof InfoIcon; hue: IconHueName }>;

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
          variant="outlined"
          icon={
            <IconChip
              icon={statusChip[toast?.severity ?? 'success'].icon}
              hue={statusChip[toast?.severity ?? 'success'].hue}
            />
          }
          sx={{ bgcolor: 'background.paper', color: 'text.primary', alignItems: 'center', boxShadow: 3 }}
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
