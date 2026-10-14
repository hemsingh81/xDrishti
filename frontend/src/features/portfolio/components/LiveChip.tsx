import { Box, Chip } from '@mui/material';
import { keyframes } from '@mui/material/styles';
import type { ChipKind, LiveChip as LiveChipState } from '../lib/liveChip';

const pulse = keyframes`
  0% { box-shadow: 0 0 0 0 currentColor; opacity: 1; }
  70% { box-shadow: 0 0 0 5px transparent; opacity: 0.7; }
  100% { box-shadow: 0 0 0 0 transparent; opacity: 1; }
`;

const colours: Record<ChipKind, 'success' | 'info' | 'warning' | 'default'> = {
  live: 'success',
  preopen: 'info',
  delayed: 'warning',
  quotes: 'default',
  closed: 'default',
};

/** A dot that pulses while the prices are live (the prototype's streaming dot); still under `prefers-reduced-motion`. */
function Dot({ pulsing }: { pulsing: boolean }) {
  return (
    <Box
      component="span"
      aria-hidden
      sx={{
        width: 8,
        height: 8,
        borderRadius: '50%',
        bgcolor: 'currentColor',
        ml: '8px !important',
        mr: '-2px !important',
        '@media (prefers-reduced-motion: no-preference)': pulsing
          ? { animation: `${pulse.toString()} 1.6s infinite` }
          : {},
      }}
    />
  );
}

/**
 * The state of the prices: Live, Pre-open, Delayed, Quotes or Closed. The text is the state in words (colour is not the
 * signal), the accessible name adds what it means, and it is not a live region: it is read when the owner gets to it.
 */
export function LiveChip({ chip }: { chip: LiveChipState }) {
  return (
    <Chip
      role="img"
      aria-label={`${chip.label}. ${chip.description}`}
      size="small"
      variant="outlined"
      color={colours[chip.kind]}
      icon={chip.kind === 'live' || chip.kind === 'delayed' ? <Dot pulsing={chip.kind === 'live'} /> : undefined}
      label={chip.label}
      sx={{ fontWeight: 600, flexShrink: 0 }}
    />
  );
}
