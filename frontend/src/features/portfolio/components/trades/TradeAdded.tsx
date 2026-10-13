import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import { Button, Stack, Typography } from '@mui/material';
import type { Ref } from 'react';
import type { Trade } from '@/shared/api';
import { formatDate, formatInrPrecise } from '@/shared/lib/format';
import { AppDialogBody, AppDialogFooter } from '@/shared/ui/dialog';
import { countOf } from '../../lib/tradeText';

interface TradeAddedProps {
  trade: Trade;
  /** Whether the trade's holding is known: Rebuild lots is offered only then. */
  canRebuild: boolean;
  headingRef: Ref<HTMLHeadingElement>;
  onRebuild: () => void;
  onAnother: () => void;
  onClose: () => void;
}

/** After a trade was added: what was stored, that no lot has changed, and the offer to rebuild the holding's lots now. */
export function TradeAdded({ trade, canRebuild, headingRef, onRebuild, onAnother, onClose }: TradeAddedProps) {
  return (
    <>
      <AppDialogBody>
        <Stack spacing={1.5} role="status">
          <Stack direction="row" sx={{ alignItems: 'center', gap: 1 }}>
            <CheckCircleIcon color="success" aria-hidden />
            <Typography ref={headingRef} component="h3" variant="h3" tabIndex={-1}>
              Trade added
            </Typography>
          </Stack>
          <Typography className="numeric">
            {`${trade.side === 'sell' ? 'Sell' : 'Buy'} ${countOf(trade.qty, 'share')} of ${trade.symbol} at ${formatInrPrecise(trade.price)} on ${formatDate(trade.tradeDate)}.`}
          </Typography>
          <Typography color="text.secondary">
            {canRebuild
              ? 'No lot has changed. Rebuild the lots of this holding to use the trade, after seeing what would change.'
              : 'No lot has changed. Open the holding and use Rebuild lots in its Trades section to use the trade.'}
          </Typography>
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onAnother}>Add another</Button>
        {canRebuild && (
          <Button variant="outlined" onClick={onRebuild}>
            Rebuild lots
          </Button>
        )}
        <Button variant="contained" onClick={onClose}>
          Done
        </Button>
      </AppDialogFooter>
    </>
  );
}
