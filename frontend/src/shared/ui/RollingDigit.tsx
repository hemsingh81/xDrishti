import { Box, keyframes } from '@mui/material';
import { useState } from 'react';
import { motion } from '@/theme';

/** Up by one glyph: the strip holds the old digit above the new one and slides until the new one is in view. */
const roll = keyframes`
  from { transform: translateY(0); }
  to { transform: translateY(-50%); }
`;

interface RollingDigitProps {
  /** One character, "0" to "9". */
  value: string;
  /** Roll to a new value (off for `prefers-reduced-motion`: the digit just changes). */
  animate?: boolean;
}

/**
 * One digit of an odometer: a column clipped to a single line. When the value changes, only this column rolls up from
 * the old digit to the new one. Decorative (hidden from assistive technology); the caller supplies the text. The width
 * is fixed (tabular figures), so the neighbours never move.
 */
export function RollingDigit({ value, animate = true }: RollingDigitProps) {
  // The digit shown before this one, kept while the roll runs (state set while rendering, the documented pattern).
  const [last, setLast] = useState(value);
  const [before, setBefore] = useState(value);
  if (value !== last) {
    setLast(value);
    setBefore(last);
  }
  const rolling = animate && before !== value;

  return (
    <Box
      component="span"
      aria-hidden
      data-value={value}
      data-rolling={rolling ? 'true' : 'false'}
      sx={{
        display: 'inline-block',
        position: 'relative',
        width: '1ch',
        height: '1.25em',
        lineHeight: '1.25em',
        overflow: 'hidden',
        verticalAlign: 'bottom',
        textAlign: 'center',
      }}
    >
      {rolling ? (
        // Keyed by the value, so every change starts the roll again.
        <Box
          key={value}
          component="span"
          onAnimationEnd={() => {
            setBefore(value);
          }}
          sx={{
            display: 'block',
            animation: `${roll.toString()} ${motion.base} ${motion.easing} forwards`,
          }}
        >
          <Box component="span" sx={{ display: 'block' }}>
            {before}
          </Box>
          <Box component="span" sx={{ display: 'block' }}>
            {value}
          </Box>
        </Box>
      ) : (
        value
      )}
    </Box>
  );
}
