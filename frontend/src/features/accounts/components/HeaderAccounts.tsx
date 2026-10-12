import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import PeopleIcon from '@mui/icons-material/PeopleOutlined';
import { Badge, Box, Button, Divider, Link as MuiLink, Popover, Stack, Tooltip, Typography } from '@mui/material';
import { useId, useState } from 'react';
import { Link } from 'react-router';
import { numeric } from '@/theme';
import { useAccountStates } from '../hooks/useAccountStates';
import { connectionStatusLabels } from '../lib/connection';
import { accountsNeedingAction } from '../lib/needingAction';
import { HeaderAccountRow } from './HeaderAccountRow';

/** Fixed width so the label arriving (or changing) does not move the other top-bar controls. */
const triggerSx = {
  minWidth: { xs: 64, sm: 250 },
  justifyContent: 'space-between',
  textTransform: 'none',
  // On phones the chevron goes, so icon + count fit in 64 px and the top bar stays within 360 px.
  '& .MuiButton-endIcon': { display: { xs: 'none', sm: 'inherit' } },
} as const;

const plural = (count: number, word: string) => `${count} ${word}${count === 1 ? '' : 's'}`;

/**
 * Top-bar control: a summary of all accounts ("2 accounts · 1 needs connecting", just the count on phones) that opens a
 * list with each account's status and a Connect / Reconnect button for those that are not connected. Quiet while
 * loading and on errors: the trigger keeps its size and says so in small text.
 */
export function HeaderAccounts() {
  const {
    query: { data: accounts, error, isPending, refetch },
    states: rows,
  } = useAccountStates();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const popoverId = useId();
  const { count: needing, expired, items } = accountsNeedingAction(rows);

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
  // The label cannot fit the names, so they go in the accessible name and the tooltip, with the state in words.
  const details = items.map((i) => `${i.name}: ${connectionStatusLabels[i.status].toLowerCase()}`).join('; ');
  const close = () => {
    setAnchor(null);
  };

  return (
    <>
      <Tooltip title={details} describeChild>
        <Button
          color="inherit"
          size="small"
          startIcon={
            <Badge variant="dot" color={expired ? 'error' : 'warning'} invisible={needing === 0} overlap="circular">
              <PeopleIcon />
            </Badge>
          }
          endIcon={<ExpandMoreIcon />}
          disabled={isPending}
          aria-haspopup="dialog"
          aria-expanded={anchor !== null}
          aria-controls={anchor ? popoverId : undefined}
          aria-label={`Accounts: ${label}${needing > 0 ? ` — needs reconnecting: ${details}` : ''}`}
          onClick={(event) => {
            setAnchor(event.currentTarget);
          }}
          sx={triggerSx}
        >
          <Box
            component="span"
            sx={{
              display: { xs: 'none', sm: 'block' },
              flex: 1,
              minWidth: 0,
              textAlign: 'left',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              ...numeric,
            }}
          >
            {label}
          </Box>
          <Box component="span" aria-hidden sx={{ display: { xs: 'inline', sm: 'none' }, flex: 1 }}>
            {short}
          </Box>
        </Button>
      </Tooltip>
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
          {needing > 0 && (
            <Typography variant="body2" color="text.secondary" sx={{ pb: 1 }}>
              Reconnect to keep holdings and data up to date.
            </Typography>
          )}
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
