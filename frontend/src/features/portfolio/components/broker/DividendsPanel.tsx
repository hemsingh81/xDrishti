import AddIcon from '@mui/icons-material/Add';
import { Box, Button, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import type { DividendView } from '@/shared/api';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { accountDividendsList } from '../../api/dividendQueries';
import { useFocusRequest } from '../../hooks/useFocusRequest';
import { AddDividendDialog } from '../income/AddDividendDialog';
import { DividendList } from '../income/DividendList';
import { VoidDividendDialog } from '../income/VoidDividendDialog';
import { SuggestionsList } from './SuggestionsList';

interface DividendsPanelProps {
  account: { id: string; name: string };
  /** Open the Add dividend form at once (the invitation under the tiles leads here). */
  startAdding: boolean;
}

/**
 * The dividends of one account on the Cash & dividends panel: the ones you entered (every holding's and the unallocated ones),
 * Add dividend, and the ledger entries that look like a dividend, to accept or dismiss. Mount it per account (`key`).
 */
export function DividendsPanel({ account, startAdding }: DividendsPanelProps) {
  const list = useInfiniteRows(accountDividendsList(account.id));
  const [adding, setAdding] = useState(startAdding);
  const [voiding, setVoiding] = useState<DividendView | null>(null);
  const [headingRef, focusHeading] = useFocusRequest<HTMLHeadingElement>();
  return (
    <Stack spacing={2}>
      <SuggestionsList accountId={account.id} />
      <Box component="section" aria-label="Dividends entered">
        <Stack
          direction="row"
          sx={{ alignItems: 'center', justifyContent: 'space-between', gap: 1, flexWrap: 'wrap', mb: 0.5 }}
        >
          <Typography ref={headingRef} variant="h3" tabIndex={-1}>
            Dividends you entered
          </Typography>
          <Button
            size="small"
            variant="contained"
            startIcon={<AddIcon />}
            onClick={() => {
              setAdding(true);
            }}
          >
            Add dividend
          </Button>
        </Stack>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          A dividend without a holding counts for the account only (unallocated). A voided dividend stays in the list
          and stops counting.
        </Typography>
        <DividendList
          list={list}
          owner={account.name}
          showHolding
          emptyText="No dividends entered for this account yet."
          onVoid={setVoiding}
        />
      </Box>
      {adding && (
        <AddDividendDialog
          accounts={[account]}
          initialAccountId={account.id}
          onClose={() => {
            setAdding(false);
          }}
        />
      )}
      {voiding && (
        <VoidDividendDialog
          dividend={voiding}
          onVoided={focusHeading}
          onClose={() => {
            setVoiding(null);
          }}
        />
      )}
    </Stack>
  );
}
