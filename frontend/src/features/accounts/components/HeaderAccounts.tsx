import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import PeopleIcon from '@mui/icons-material/PeopleOutlined';
import { Box, Button, Divider, Link as MuiLink, Popover, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useId, useState } from 'react';
import { Link } from 'react-router';
import { accountsQuery } from '../api/accountsQueries';
import { useNow } from '../hooks/useNow';
import { connectionState, needsClock } from '../lib/connection';
import { HeaderAccountRow } from './HeaderAccountRow';

/** Fixed width so the label arriving (or changing) does not move the other top-bar controls. */
const triggerSx = { minWidth: { xs: 64, sm: 250 }, justifyContent: 'space-between', textTransform: 'none' } as const;

const plural = (count: number, word: string) => `${count} ${word}${count === 1 ? '' : 's'}`;

/**
 * Top-bar control: a summary of all accounts ("2 accounts · 1 needs connecting", just the count on phones) that opens a
 * list with each account's status and a Connect / Reconnect button for those that are not connected. Quiet while
 * loading and on errors: the trigger keeps its size and says so in small text.
 */
export function HeaderAccounts() {
  const { data: accounts, error, isPending, refetch } = useQuery(accountsQuery);
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const popoverId = useId();
  const connections = accounts?.map((a) => a.connection) ?? [];
  const now = useNow(connections.some(needsClock), connections.map((c) => c.expiresAt ?? '').join('|'));
  const rows = (accounts ?? []).map((account) => ({
    account,
    status: connectionState(account.connection, now).status,
  }));
  const needing = rows.filter((row) => row.status !== 'connected').length;

  let label = 'Accounts';
  let short = '…';
  if (error) {
    label = 'Accounts unavailable';
    short = '!';
  } else if (accounts) {
    short = String(accounts.length);
    if (accounts.length === 0) {
      label = 'No accounts yet';
    } else {
      label = `${plural(accounts.length, 'account')} · ${needing > 0 ? `${needing} needs connecting` : 'all connected'}`;
    }
  }
  const close = () => {
    setAnchor(null);
  };

  return (
    <>
      <Button
        color="inherit"
        size="small"
        startIcon={<PeopleIcon />}
        endIcon={<ExpandMoreIcon />}
        disabled={isPending}
        aria-haspopup="dialog"
        aria-expanded={anchor !== null}
        aria-controls={anchor ? popoverId : undefined}
        aria-label={`Accounts: ${label}`}
        onClick={(event) => {
          setAnchor(event.currentTarget);
        }}
        sx={triggerSx}
      >
        <Box component="span" sx={{ display: { xs: 'none', sm: 'inline' }, flex: 1, textAlign: 'left' }}>
          {label}
        </Box>
        <Box component="span" aria-hidden sx={{ display: { xs: 'inline', sm: 'none' }, flex: 1 }}>
          {short}
        </Box>
      </Button>
      <Popover
        id={popoverId}
        open={anchor !== null}
        anchorEl={anchor}
        onClose={close}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
        slotProps={{
          paper: { role: 'dialog', 'aria-label': 'Accounts', sx: { width: 380, maxWidth: 'calc(100vw - 16px)' } },
        }}
      >
        <Stack sx={{ p: 2 }}>
          {error && (
            <Stack spacing={1} sx={{ alignItems: 'flex-start' }}>
              <Typography variant="body2" role="alert">
                Could not load the accounts.
              </Typography>
              <Button
                size="small"
                onClick={() => {
                  void refetch();
                }}
              >
                Try again
              </Button>
            </Stack>
          )}
          {accounts?.length === 0 && (
            <Stack spacing={1} sx={{ alignItems: 'flex-start' }}>
              <Typography variant="body2" color="text.secondary">
                No accounts yet. Add your Dhan account to start.
              </Typography>
              <MuiLink component={Link} to="/accounts" onClick={close}>
                Add account
              </MuiLink>
            </Stack>
          )}
          {rows.length > 0 && (
            <Stack
              component="ul"
              aria-label="Account connections"
              divider={<Divider component="li" />}
              sx={{ m: 0, p: 0, listStyle: 'none' }}
            >
              {rows.map(({ account, status }) => (
                <HeaderAccountRow key={account.id} account={account} status={status} onNavigate={close} />
              ))}
            </Stack>
          )}
          {accounts && accounts.length > 0 && (
            <>
              <Divider />
              <MuiLink component={Link} to="/accounts" onClick={close} sx={{ pt: 1.5 }}>
                Manage accounts
              </MuiLink>
            </>
          )}
        </Stack>
      </Popover>
    </>
  );
}
