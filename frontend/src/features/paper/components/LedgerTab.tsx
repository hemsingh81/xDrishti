import { MenuItem, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { ErrorState, LoadingState } from '@/shared/ui';
import { paperAccountsQuery } from '../api/paperQueries';
import { AccountTiles } from './AccountTiles';
import { BasisNote } from './BasisNote';
import { LedgerBody } from './LedgerBody';

/** The Ledger tab: one paper account's figures, open positions, closed trades and realised-equity line. Read only. */
export function LedgerTab() {
  const accounts = useQuery(paperAccountsQuery);
  const [picked, setPicked] = useState<string>();

  if (accounts.isPending) return <LoadingState label="Loading paper accounts…" />;
  if (accounts.isError) return <ErrorState error={accounts.error} />;
  const items = accounts.data.items;
  const account = items.find((a) => a.accountId === picked) ?? items[0];
  if (!account) {
    return (
      <>
        <BasisNote basis={accounts.data.basis} limits={[]} />
        <Typography>No paper account yet.</Typography>
      </>
    );
  }
  return (
    <>
      <BasisNote basis={accounts.data.basis} limits={account.limits} />
      <TextField
        select
        size="small"
        label="Paper account"
        value={account.accountId}
        onChange={(event) => {
          setPicked(event.target.value);
        }}
        sx={{ minWidth: 260, mb: 2 }}
      >
        {items.map((a) => (
          <MenuItem key={a.accountId} value={a.accountId}>
            {a.name}
          </MenuItem>
        ))}
      </TextField>
      <AccountTiles account={account} />
      <LedgerBody key={account.accountId} accountId={account.accountId} />
    </>
  );
}
