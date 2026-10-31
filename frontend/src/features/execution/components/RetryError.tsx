import { Alert, Button } from '@mui/material';
import { problemText } from '../api/confirmApi';

/** A failed read: the problem title and a retry (criterion 12). */
export function RetryError({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  return (
    <Alert
      severity="error"
      sx={{ my: 2 }}
      action={
        <Button color="inherit" size="small" onClick={onRetry}>
          Retry
        </Button>
      }
    >
      {problemText(error)}
    </Alert>
  );
}
