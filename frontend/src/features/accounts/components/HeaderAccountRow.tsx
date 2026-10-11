import LoginIcon from '@mui/icons-material/Login';
import { Box, Button, Link as MuiLink, Stack, Typography } from '@mui/material';
import { Link } from 'react-router';
import type { AccountSummary } from '@/shared/api';
import { useStartConnect } from '../api/connectionMutations';
import { connectionProblem } from '../lib/connectErrors';
import { appConfigured, connectLabel, type ConnectionStatusKey } from '../lib/connection';
import { ConnectionStatus } from './ConnectionStatus';

interface HeaderAccountRowProps {
  account: AccountSummary;
  /** The status against the clock (a passed expiry reads "expired"). */
  status: ConnectionStatusKey;
  /** Called when the owner follows a link, so the menu can close. */
  onNavigate: () => void;
}

/** One account in the header menu: colour dot, name, status, and what to do next when it is not connected. */
export function HeaderAccountRow({ account, status, onNavigate }: HeaderAccountRowProps) {
  const connect = useStartConnect();
  const message = connectionProblem(connect.error);
  const needsConnecting = status !== 'connected';

  return (
    <Stack spacing={0.75} component="li" sx={{ py: 1.25 }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
        <Box
          aria-hidden
          // The account's colour is user data stored with the account, not a theme token.
          sx={{ width: 10, height: 10, borderRadius: '50%', flex: 'none', bgcolor: account.color }}
        />
        <Typography sx={{ fontWeight: 650, overflowWrap: 'anywhere' }}>{account.name}</Typography>
      </Stack>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 1 }}>
        <ConnectionStatus connection={account.connection} />
        {needsConnecting &&
          (appConfigured(account) ? (
            <Button
              size="small"
              variant="outlined"
              startIcon={<LoginIcon />}
              aria-label={`${connectLabel(status)} ${account.name}`}
              // After a successful start the browser is on its way to Dhan: keep the spinner until it leaves.
              loading={connect.isPending}
              onClick={() => {
                connect.mutate(account.id);
              }}
            >
              {connectLabel(status)}
            </Button>
          ) : (
            <MuiLink
              component={Link}
              to={`/accounts?account=${account.id}&tab=connection`}
              onClick={onNavigate}
              variant="body2"
              aria-label={`Add API key & secret for ${account.name}`}
            >
              Add API key &amp; secret
            </MuiLink>
          ))}
      </Stack>
      {message && (
        <Typography role="alert" variant="body2" color="error">
          {message}
        </Typography>
      )}
    </Stack>
  );
}
