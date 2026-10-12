import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Link, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import type { DhanOverview } from '@/shared/api';
import { PillLabel } from './RibbonPill';
import { pillSx } from './ribbonStyle';

const statusText: Readonly<Record<string, string>> = {
  connected: 'Connected',
  expired: 'Sign-in expired',
  notConnected: 'Not connected',
};

/** The reconnect (or set-up) link of a Data account that cannot fetch. */
const accountLink = (dhan: DhanOverview) => (dhan.account ? `/accounts?account=${dhan.account.id}` : '/accounts');

/**
 * The Data account (the one the Dhan step fetches with) and whether it can fetch. When it cannot, the reason is given
 * in words and a link leads to the account to reconnect it.
 */
export function AccountPill({ dhan }: { dhan: DhanOverview }) {
  const { account, canFetch, reason } = dhan;
  const connected = account?.status === 'connected';
  const Icon = connected ? CheckCircleIcon : WarningIcon;
  const label = account ? (statusText[account.status] ?? account.status) : 'No Data account';
  return (
    <Box sx={pillSx(canFetch && connected ? 'neutral' : 'warning')}>
      <PillLabel>Data account</PillLabel>
      {account && (
        <Box component="span" sx={{ fontWeight: 700, overflowWrap: 'anywhere' }}>
          {account.name}
        </Box>
      )}
      <Box
        component="span"
        sx={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: 0.5,
          color: connected ? 'success.main' : 'warning.main',
        }}
      >
        <Icon fontSize="small" aria-hidden />
        <Typography component="span" variant="body2" sx={{ fontWeight: 600, color: 'inherit' }}>
          {label}
        </Typography>
      </Box>
      {!canFetch && (
        <>
          {reason && (
            <Typography component="span" variant="body2">
              {reason}
            </Typography>
          )}
          <Link component={RouterLink} to={accountLink(dhan)}>
            {account ? 'Reconnect' : 'Open Accounts'}
          </Link>
        </>
      )}
    </Box>
  );
}
