import { Box, Tooltip } from '@mui/material';
import { useState, type ReactNode } from 'react';
import type { Returns } from '@/shared/api';
import { arrowOf, directionOf, noPriceReason, signedInr, signedPercent, xirrOf, type Direction } from '../lib/figures';

const colours: Record<Direction, string> = { up: 'success.main', down: 'error.main', flat: 'text.primary' };

/**
 * A gain or a loss: the sign is part of the text and an arrow (up for a gain, down for a loss) stands before it, so the
 * meaning never rests on the colour alone.
 */
export function Trend({ direction, children }: { direction: Direction; children: ReactNode }) {
  const arrow = arrowOf(direction);
  return (
    <Box component="span" sx={{ color: colours[direction], whiteSpace: 'nowrap' }}>
      {arrow && (
        <Box component="span" aria-hidden sx={{ mr: 0.5, fontSize: '0.75em' }}>
          {arrow}
        </Box>
      )}
      {children}
    </Box>
  );
}

/**
 * A figure that is not there ("—", or "n/m") with the reason. The reason is the element's accessible name (so a screen
 * reader says it) and a tooltip; `focusable` also makes it a tab stop so the tooltip opens from the keyboard (used where
 * there is only one or a few, not in every grid row).
 */
export function Unavailable({
  label,
  reason,
  focusable = false,
}: {
  label: string;
  reason: string;
  focusable?: boolean;
}) {
  // Opened by hover as usual, and by keyboard focus too (the tooltip must not depend on the pointer).
  const [open, setOpen] = useState(false);
  return (
    <Tooltip
      title={reason}
      open={open}
      onOpen={() => {
        setOpen(true);
      }}
      onClose={() => {
        setOpen(false);
      }}
    >
      <Box
        component="span"
        role="img"
        tabIndex={focusable ? 0 : undefined}
        onFocus={() => {
          setOpen(true);
        }}
        onBlur={() => {
          setOpen(false);
        }}
      >
        {label}
      </Box>
    </Tooltip>
  );
}

/** Profit or loss in rupees: "▲ +₹1,200" / "▼ −₹4,000"; "—" with the reason when there is no value. */
export function Pnl({ value, focusable = false }: { value: number | null; focusable?: boolean }) {
  if (value === null) return <Unavailable label="—" reason={noPriceReason} focusable={focusable} />;
  return <Trend direction={directionOf(value)}>{signedInr(value)}</Trend>;
}

/** Profit or loss in percent; "—" when there is no cost to compare with (or no price). */
export function PnlPercent({ value }: { value: number | null }) {
  if (value === null) return <>—</>;
  return <Trend direction={directionOf(value, 2)}>{signedPercent(value)}</Trend>;
}

/** Annualised return; "—" (or "n/m") with the reason when the API could not give one. */
export function Xirr({
  returns,
  focusable = false,
}: {
  returns: Pick<Returns, 'xirr' | 'xirrState'>;
  focusable?: boolean;
}) {
  const xirr = xirrOf(returns);
  if ('value' in xirr) return <Trend direction={directionOf(xirr.value, 2)}>{signedPercent(xirr.value)}</Trend>;
  return <Unavailable label={xirr.label} reason={xirr.reason} focusable={focusable} />;
}
