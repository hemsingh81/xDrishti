import CloseIcon from '@mui/icons-material/Close';
import { Alert, Box, Divider, Drawer, IconButton, Stack, TextField, Typography } from '@mui/material';
import { useId, useState } from 'react';
import type { AccountSummary } from '@/shared/api';
import { initialTradesAccount } from '../../lib/accounts';
import { noLedgerFilter, type LedgerFilter } from '../../lib/ledger';
import { CashView } from './CashView';
import { DividendsPanel } from './DividendsPanel';

interface CashDrawerProps {
  onClose: () => void;
  /** The accounts with the Portfolio role. */
  accounts: readonly AccountSummary[];
  /** The account of the page's scope (null = all accounts). */
  scopeAccountId: string | null;
  /** Open the Add dividend form at once (the invitation under the tiles leads here). */
  startAdding?: boolean;
}

/**
 * Cash & dividends in a side panel: the dividends you entered for the account (add, void, the suggestions of the ledger) and
 * what Dhan's ledger says about its money (funds in and out, dividends, charges), by class and period. The ledger's costs are
 * part of the net returns from the day the ledger was first read; its other entries are shown only.
 */
export function CashDrawer({ onClose, accounts, scopeAccountId, startAdding = false }: CashDrawerProps) {
  const titleId = useId();
  const [accountId, setAccountId] = useState(() => initialTradesAccount(accounts, scopeAccountId));
  const [filter, setFilter] = useState<LedgerFilter>(noLedgerFilter);
  const account = accounts.find((a) => a.id === accountId);
  return (
    <Drawer
      anchor="right"
      open
      onClose={onClose}
      slotProps={{
        paper: {
          role: 'dialog',
          'aria-labelledby': titleId,
          sx: { width: { xs: '100%', md: 920 }, maxWidth: '100vw' },
        },
      }}
    >
      <Stack direction="row" sx={{ alignItems: 'center', gap: 1, px: 2.5, py: 1.5 }}>
        <Typography id={titleId} component="h2" variant="h2" sx={{ flex: 1 }}>
          Cash &amp; dividends
        </Typography>
        <IconButton aria-label="Close cash and dividends" size="small" edge="end" onClick={onClose}>
          <CloseIcon fontSize="small" />
        </IconButton>
      </Stack>
      <Divider />
      <Stack spacing={2} sx={{ px: 2.5, pt: 1.5, overflowY: 'auto', flex: 1, pb: 3 }}>
        <Alert severity="info">
          Dhan credits dividends to your bank account, so they rarely appear here &mdash; enter them yourself. Dividends
          you enter and the ledger&rsquo;s charges count in the net return and net XIRR; the other entries are shown
          only.
        </Alert>
        {accounts.length > 1 && (
          <TextField
            select
            label="Account"
            value={accountId}
            onChange={(event) => {
              setAccountId(event.target.value);
            }}
            slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
            sx={{ maxWidth: 360 }}
          >
            <option value="" disabled>
              Choose an account
            </option>
            {accounts.map((account) => (
              <option key={account.id} value={account.id}>
                {account.name}
              </option>
            ))}
          </TextField>
        )}
        {account && <DividendsPanel key={`dividends-${account.id}`} account={account} startAdding={startAdding} />}
        {accountId ? (
          <CashView key={accountId} accountId={accountId} filter={filter} onFilter={setFilter} />
        ) : (
          <Box>
            <Typography color="text.secondary">Choose an account to see its cash movements.</Typography>
          </Box>
        )}
      </Stack>
    </Drawer>
  );
}
