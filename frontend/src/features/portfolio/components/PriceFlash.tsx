import { Box, useMediaQuery } from '@mui/material';
import { alpha, keyframes } from '@mui/material/styles';
import { useEffect, useState, type ReactNode } from 'react';

/** How long a changed price stays marked. */
export const flashMs = 1000;

const fade = keyframes`
  from { background-color: var(--flash-from); }
  to { background-color: transparent; }
`;

type Direction = 'up' | 'down';

/**
 * Marks a price that just changed: for one second its cell takes a tint of green (up) or red (down) and an arrow stands
 * before it, so the direction never rests on the colour. Under `prefers-reduced-motion` nothing is marked at all - the new
 * price simply replaces the old one. The mark is decoration (`aria-hidden`); the price itself is the information.
 */
export function PriceFlash({ price, children }: { price: number; children: ReactNode }) {
  const reduced = useMediaQuery('(prefers-reduced-motion: reduce)');
  const [seen, setSeen] = useState(price);
  const [flash, setFlash] = useState<Direction | null>(null);

  // A new price is noticed while rendering (not in an effect), so the first frame of the new price already carries the mark.
  if (price !== seen) {
    setSeen(price);
    setFlash(reduced ? null : price > seen ? 'up' : 'down');
  }
  useEffect(() => {
    if (flash === null) return;
    const timer = setTimeout(() => {
      setFlash(null);
    }, flashMs);
    return () => {
      clearTimeout(timer);
    };
  }, [flash, price]);

  return (
    <Box
      component="span"
      data-flash={flash ?? undefined}
      // A new price restarts the animation: the key changes with it, and only while a mark is showing.
      key={flash === null ? 'still' : `${flash}-${String(price)}`}
      sx={[
        { borderRadius: 0.5, px: 0.5, mx: -0.5, whiteSpace: 'nowrap' },
        flash !== null &&
          ((theme) => ({
            '--flash-from': alpha(flash === 'up' ? theme.palette.success.main : theme.palette.error.main, 0.28),
            color: flash === 'up' ? theme.palette.success.main : theme.palette.error.main,
            animation: `${fade.toString()} ${String(flashMs)}ms ease-out forwards`,
          })),
      ]}
    >
      {flash !== null && (
        <Box component="span" aria-hidden sx={{ mr: 0.5, fontSize: '0.75em' }}>
          {flash === 'up' ? '▲' : '▼'}
        </Box>
      )}
      {children}
    </Box>
  );
}
