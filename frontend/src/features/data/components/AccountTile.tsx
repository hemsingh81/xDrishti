import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import PeopleIcon from '@mui/icons-material/PeopleOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Chip, Link, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import type { DhanOverview } from '@/shared/api';
import { StatTile } from '@/shared/ui';

const statusText: Readonly<Record<string, string>> = {
  connected: 'Connected',
  expired: 'Sign-in expired',
  notConnected: 'Not connected',
};

/**
 * The Data account (the one the Dhan step fetches with) and whether it can fetch. When it cannot, the reason is given
 * in words and a link leads to the account to reconnect it.
 */
export function AccountTile({ dhan }: { dhan: DhanOverview }) {
  const { account, canFetch, reason } = dhan;
  const connected = account?.status === 'connected';
  const label = account ? (statusText[account.status] ?? account.status) : 'No Data account';
  return (
    <StatTile
      label="Data account"
      icon={PeopleIcon}
      hue="accounts"
      value={
        <Box component="span" sx={{ fontSize: '1rem', overflowWrap: 'anywhere' }}>
          {account?.name ?? 'None set up'}
        </Box>
      }
      hint={
        <Stack spacing={0.5} sx={{ alignItems: 'flex-start', mt: 0.5 }}>
          <Chip
            size="small"
            variant="outlined"
            color={connected ? 'success' : 'warning'}
            icon={connected ? <CheckCircleIcon /> : <WarningIcon />}
            label={label}
          />
          {!canFetch && (
            <>
              {reason && <Typography variant="body2">{reason}</Typography>}
              <Link component={RouterLink} to={account ? `/accounts?account=${account.id}` : '/accounts'}>
                {account ? 'Reconnect' : 'Open Accounts'}
              </Link>
            </>
          )}
        </Stack>
      }
    />
  );
}
