import { Box, Tooltip, Typography } from '@mui/material';
import { useState } from 'react';
import type { TradeRebuild } from '@/shared/api';
import { costDeltaText, costNoteText, costWhy } from '../../lib/tradeText';

/**
 * Under a security's status: that the cost basis of the trades differs from the broker's average (when it does by a rupee
 * or more) and, when the opening lot's cost had to fall back on the average cost, that the cost is approximated. The
 * explanation is a tooltip that opens from the keyboard as well as the pointer.
 */
export function CostNote({ rebuild, sells }: { rebuild: TradeRebuild; sells: number }) {
  const [open, setOpen] = useState(false);
  const delta = costDeltaText(rebuild);
  const note = costNoteText(rebuild);
  if (!delta && !note) return null;
  return (
    <Box sx={{ mt: 0.5 }}>
      {delta && (
        <Tooltip
          title={costWhy(rebuild, sells)}
          open={open}
          onOpen={() => {
            setOpen(true);
          }}
          onClose={() => {
            setOpen(false);
          }}
        >
          <Typography
            variant="caption"
            color="text.secondary"
            tabIndex={0}
            onFocus={() => {
              setOpen(true);
            }}
            onBlur={() => {
              setOpen(false);
            }}
            sx={{ display: 'block', textDecoration: 'underline dotted' }}
          >
            {delta}
          </Typography>
        </Tooltip>
      )}
      {note && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
          Cost approximated: {note}
        </Typography>
      )}
    </Box>
  );
}
