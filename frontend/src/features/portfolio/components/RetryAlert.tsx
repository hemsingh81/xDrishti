import { Alert, Button, type AlertProps } from '@mui/material';
import { problemText } from '../lib/errors';

interface RetryAlertProps {
  error: unknown;
  fallback: string;
  /** Re-runs the failed request. Omit for no Retry button. */
  onRetry?: () => unknown;
  severity?: AlertProps['severity'];
  sx?: AlertProps['sx'];
  /** Replaces the message derived from the error. */
  message?: string | null;
  /** Appended to the message. */
  suffix?: string;
}

/** A failed read in plain words with a Retry button: the one shape every portfolio section's error takes. */
export function RetryAlert({ error, fallback, onRetry, severity = 'error', sx, suffix, message }: RetryAlertProps) {
  return (
    <Alert
      severity={severity}
      sx={sx}
      action={
        onRetry && (
          <Button
            color="inherit"
            size="small"
            onClick={() => {
              void onRetry();
            }}
          >
            Retry
          </Button>
        )
      }
    >
      {`${message ?? problemText(error, fallback)}${suffix ? ` ${suffix}` : ''}`}
    </Alert>
  );
}
