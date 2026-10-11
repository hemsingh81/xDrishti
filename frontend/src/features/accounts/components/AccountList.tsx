import { Box, Link as MuiLink, List, ListItem, Stack, Typography } from '@mui/material';
import { Link } from 'react-router';
import type { AccountSummary } from '@/shared/api';
import { accountSubtitle } from '../lib/labels';
import { RoleChips } from './RoleChips';
import { TokenStatus } from './TokenStatus';

interface AccountListProps {
  accounts: AccountSummary[];
  selectedId: string;
  /** Link target that selects an account (keeps the current tab). */
  hrefFor: (id: string) => string;
}

/** The accounts, one link each; the selected one is marked with aria-current and a thicker bar. */
export function AccountList({ accounts, selectedId, hrefFor }: AccountListProps) {
  return (
    <List component="ul" aria-label="Accounts" disablePadding sx={{ display: 'grid', gap: 1 }}>
      {accounts.map((account) => {
        const selected = account.id === selectedId;
        return (
          <ListItem key={account.id} disablePadding>
            <MuiLink
              component={Link}
              to={hrefFor(account.id)}
              aria-current={selected ? 'true' : undefined}
              underline="none"
              color="inherit"
              sx={{
                display: 'block',
                width: '100%',
                p: 1.5,
                borderRadius: 2,
                border: 1,
                borderColor: selected ? 'primary.main' : 'divider',
                borderLeftWidth: 5,
                borderLeftStyle: 'solid',
                // The account's colour is user data stored with the account, not a theme token.
                borderLeftColor: account.color,
                bgcolor: selected ? 'action.selected' : 'background.paper',
                '&:hover': { bgcolor: 'action.hover' },
              }}
            >
              <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', gap: 1 }}>
                <Typography component="span" sx={{ fontWeight: 700, overflowWrap: 'anywhere' }}>
                  {account.name}
                </Typography>
                <TokenStatus status={account.tokenStatus} showLabel={false} />
              </Stack>
              <Typography variant="body2" color="text.secondary">
                {accountSubtitle(account)}
              </Typography>
              <Box sx={{ mt: 0.75 }}>
                <RoleChips roles={account.roles} />
              </Box>
              <Typography variant="body2" sx={{ mt: 0.75 }}>
                Holdings value: —
              </Typography>
            </MuiLink>
          </ListItem>
        );
      })}
    </List>
  );
}
