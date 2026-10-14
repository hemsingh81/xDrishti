import { Alert, Button } from '@mui/material';
import { ApiError } from '@/shared/api';

/** An inline error for a failed read, with Retry. */
export function QueryAlert({ error, fallback, onRetry }: { error: unknown; fallback: string; onRetry: () => void }) {
  const message =
    error instanceof ApiError
      ? (error.problem?.title ?? fallback)
      : 'Could not reach the server. Check your connection.';
  return (
    <Alert
      severity="error"
      action={
        <Button color="inherit" size="small" onClick={onRetry}>
          Retry
        </Button>
      }
    >
      {message}
    </Alert>
  );
}
