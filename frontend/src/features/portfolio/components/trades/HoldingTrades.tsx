import AddIcon from '@mui/icons-material/Add';
import { Box, Button, Stack, Typography } from '@mui/material';
import { useRef, useState } from 'react';
import type { HoldingRow, Trade } from '@/shared/api';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { useToast } from '@/shared/ui/toastContext';
import { useVoidTrade } from '../../api/tradeMutations';
import { holdingTradesList } from '../../api/tradeQueries';
import { problemText } from '../../lib/errors';
import { tradeName } from '../../lib/tradeText';
import { AddTradeDialog } from './AddTradeDialog';
import { RebuildLotsDialog } from './RebuildLotsDialog';
import { TradeList } from './TradeList';

interface HoldingTradesProps {
  holdingId: string;
  /** The holding's row; Add trade needs its account and symbol. */
  holding: HoldingRow | undefined;
}

/** The trades of a holding's security, newest first, with Void, Add trade and Rebuild lots (which shows its dry run first). */
export function HoldingTrades({ holdingId, holding }: HoldingTradesProps) {
  const { notify } = useToast();
  const list = useInfiniteRows(holdingTradesList(holdingId));
  const voidTrade = useVoidTrade();
  const [dialog, setDialog] = useState<'add' | 'rebuild' | null>(null);
  const symbol = holding?.symbol ?? 'this holding';
  const heading = useRef<HTMLHeadingElement>(null);

  // `mutateAsync` (not the callbacks of `mutate`, which only the latest call keeps): each void tells its own outcome.
  async function onVoid(trade: Trade) {
    // The Void button goes away with the trade's state: the keyboard goes to the section's title, which says where it is.
    heading.current?.focus();
    try {
      await voidTrade.mutateAsync(trade.id);
      notify(`Voided the ${tradeName(trade)}. Your lots are unchanged: rebuild them to use this.`);
    } catch (error) {
      notify(problemText(error, `Could not void the ${tradeName(trade)}. Nothing was changed.`), 'error');
    }
  }

  return (
    <Box component="section" aria-label="Trades">
      <Stack
        direction="row"
        sx={{ alignItems: 'center', justifyContent: 'space-between', gap: 1, flexWrap: 'wrap', mb: 0.5 }}
      >
        <Typography ref={heading} variant="h3" tabIndex={-1}>
          Trades
        </Typography>
        <Stack direction="row" sx={{ gap: 1 }}>
          {holding && (
            <Button
              size="small"
              variant="outlined"
              startIcon={<AddIcon />}
              onClick={() => {
                setDialog('add');
              }}
            >
              Add trade
            </Button>
          )}
          <Button
            size="small"
            variant="outlined"
            onClick={() => {
              setDialog('rebuild');
            }}
          >
            Rebuild lots
          </Button>
        </Stack>
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        Adding or voiding a trade does not change the lots. Rebuild lots shows what would change, then applies it.
      </Typography>
      <TradeList
        list={list}
        holdingName={symbol}
        onVoid={(trade) => {
          void onVoid(trade);
        }}
      />
      {dialog === 'add' && holding && (
        <AddTradeDialog
          accounts={[{ id: holding.accountId, name: holding.accountName }]}
          initialAccountId={holding.accountId}
          holding={{ id: holding.id, accountId: holding.accountId, symbol: holding.symbol, name: holding.name }}
          onClose={() => {
            setDialog(null);
          }}
        />
      )}
      {dialog === 'rebuild' && (
        <RebuildLotsDialog
          holdingId={holdingId}
          symbol={symbol}
          onClose={() => {
            setDialog(null);
          }}
        />
      )}
    </Box>
  );
}
