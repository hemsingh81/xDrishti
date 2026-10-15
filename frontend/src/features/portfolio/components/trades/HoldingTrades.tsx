import AddIcon from '@mui/icons-material/Add';
import { Alert, Box, Button, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import type { HoldingRow, Trade } from '@/shared/api';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { useToast } from '@/shared/ui/toastContext';
import { brokerHistoryStatusQuery } from '../../api/brokerHistoryQueries';
import { useVoidTrade } from '../../api/tradeMutations';
import { holdingTradesList } from '../../api/tradeQueries';
import { needsReview } from '../../lib/brokerText';
import type { HoldingRequest } from '../../hooks/useHoldingRequest';
import { problemText } from '../../lib/errors';
import { tradeName } from '../../lib/tradeText';
import { AddTradeDialog } from './AddTradeDialog';
import { RebuildLotsDialog } from './RebuildLotsDialog';
import { TradeList } from './TradeList';

interface HoldingTradesProps {
  holdingId: string;
  /** The holding's row; Add trade needs its account and symbol. */
  holding: HoldingRow | undefined;
  /** A link asked for this section (and maybe for the rebuild preview): scroll here, open the preview, then say it is done. */
  request?: HoldingRequest | null;
}

/** The trades of a holding's security, newest first, with Void, Add trade and Rebuild lots (which shows its dry run first). */
export function HoldingTrades({ holdingId, holding, request = null }: HoldingTradesProps) {
  const { notify } = useToast();
  const list = useInfiniteRows(holdingTradesList(holdingId));
  const voidTrade = useVoidTrade();
  const [dialog, setDialog] = useState<'add' | 'rebuild' | null>(request?.rebuild ? 'rebuild' : null);
  // A link used while this holding is open asks again: the preview opens once per request.
  const [asked, setAsked] = useState(request !== null);
  if (request === null ? asked : !asked) {
    setAsked(request !== null);
    if (request?.rebuild) setDialog('rebuild');
  }
  const symbol = holding?.symbol ?? 'this holding';
  const heading = useRef<HTMLHeadingElement>(null);
  // The last Dhan read left this holding's lots alone when one of them is dated by hand: say so (the status is optional here).
  const broker = useQuery({ ...brokerHistoryStatusQuery(holding?.accountId ?? ''), enabled: holding !== undefined });
  const review = holding !== undefined && broker.data !== undefined && needsReview(broker.data, holding.symbol);
  const onHandled = request?.onHandled;
  // The section is scrolled to and its title takes the keyboard (it says where the owner is); the request is then done.
  useEffect(() => {
    if (!onHandled) return;
    if (typeof heading.current?.scrollIntoView === 'function') heading.current.scrollIntoView({ block: 'start' });
    heading.current?.focus({ preventScroll: true });
    onHandled();
  }, [onHandled]);
  const hasBrokerTrades = list.rows.some((trade) => trade.source === 'broker');

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
      {review && (
        <Alert
          severity="info"
          sx={{ mb: 1 }}
          action={
            <Button
              color="inherit"
              size="small"
              onClick={() => {
                setDialog('rebuild');
              }}
            >
              Review the rebuild
            </Button>
          }
        >
          The last read from Dhan left the lots of {symbol} alone, because a lot has a date you set. Rebuild lots shows
          what the trades would change first, and replaces your dates only if you confirm.
        </Alert>
      )}
      {hasBrokerTrades && (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          Trades marked Dhan are read from Dhan every evening. A Dhan trade you void is stored again the next time its
          day is read.
        </Typography>
      )}
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
