import { Alert, Button } from '@mui/material';

interface QueryAlertProps {
  message: string;
  onRetry: () => void;
}

/** A failed read, in words, with a Retry button. */
export function QueryAlert({ message, onRetry }: QueryAlertProps) {
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
