import AddIcon from '@mui/icons-material/Add';
import { Box, Button, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import type { DividendView, HoldingRow } from '@/shared/api';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { holdingDividendsList } from '../../api/dividendQueries';
import { useFocusRequest } from '../../hooks/useFocusRequest';
import { AddDividendDialog } from './AddDividendDialog';
import { DividendList } from './DividendList';
import { VoidDividendDialog } from './VoidDividendDialog';

interface HoldingDividendsProps {
  holdingId: string;
  /** The holding's row; Add dividend needs its account and symbol. */
  holding: HoldingRow | undefined;
}

/** The dividends entered for a holding, newest first, with Add dividend and Void (which asks first). */
export function HoldingDividends({ holdingId, holding }: HoldingDividendsProps) {
  const list = useInfiniteRows(holdingDividendsList(holdingId));
  const [adding, setAdding] = useState(false);
  const [voiding, setVoiding] = useState<DividendView | null>(null);
  const [headingRef, focusHeading] = useFocusRequest<HTMLHeadingElement>();
  const symbol = holding?.symbol ?? 'this holding';
  return (
    <Box component="section" aria-label="Dividends">
      <Stack
        direction="row"
        sx={{ alignItems: 'center', justifyContent: 'space-between', gap: 1, flexWrap: 'wrap', mb: 0.5 }}
      >
        <Typography ref={headingRef} variant="h3" tabIndex={-1}>
          Dividends
        </Typography>
        {holding && (
          <Button
            size="small"
            variant="outlined"
            startIcon={<AddIcon />}
            onClick={() => {
              setAdding(true);
            }}
          >
            Add dividend
          </Button>
        )}
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        Dividends are entered by you and count in total return and net XIRR from the date you enter. A voided dividend
        stays in the list and stops counting.
      </Typography>
      <DividendList
        list={list}
        owner={symbol}
        showHolding={false}
        emptyText="No dividends entered for this holding yet. Add the ones you received."
        onVoid={setVoiding}
      />
      {adding && holding && (
        <AddDividendDialog
          accounts={[{ id: holding.accountId, name: holding.accountName }]}
          initialAccountId={holding.accountId}
          holding={{ id: holding.id, symbol: holding.symbol, name: holding.name }}
          onClose={() => {
            setAdding(false);
          }}
        />
      )}
      {voiding && (
        <VoidDividendDialog
          dividend={voiding}
          // The Void button goes away with the dividend's state: the keyboard goes to the section's title.
          onVoided={focusHeading}
          onClose={() => {
            setVoiding(null);
          }}
        />
      )}
    </Box>
  );
}
