import { Alert, Button } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import { accountTabPath, problemKind, problemText } from '../lib/errors';

interface ProblemNoticeProps {
  error: unknown;
  /** The account the action was for: the link to fix a connection problem points at it. */
  accountId: string | null;
  fallback: string;
  onRetry?: () => void;
}

/** A failed portfolio request in plain words, with the way out: the account's Connection tab, its Roles, or Retry. */
export function ProblemNotice({ error, accountId, fallback, onRetry }: ProblemNoticeProps) {
  const kind = problemKind(error);
  const to =
    accountId && (kind === 'not_connected' || kind === 'token_expired')
      ? { path: accountTabPath(accountId, 'connection'), label: 'Open the Connection tab' }
      : accountId && (kind === 'role_off' || kind === 'no_bucket')
        ? { path: accountTabPath(accountId, 'roles'), label: 'Open the Roles tab' }
        : null;
  const retry = !to && kind !== 'sync_running' && onRetry;
  return (
    <Alert
      severity={kind === 'sync_running' ? 'info' : 'error'}
      action={
        to ? (
          <Button component={RouterLink} to={to.path} color="inherit" size="small">
            {to.label}
          </Button>
        ) : retry ? (
          <Button color="inherit" size="small" onClick={onRetry}>
            Retry
          </Button>
        ) : undefined
      }
    >
      {problemText(error, fallback)}
    </Alert>
  );
}
