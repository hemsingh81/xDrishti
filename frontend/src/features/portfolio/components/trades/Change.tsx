import { Box } from '@mui/material';

const hidden = {
  position: 'absolute',
  width: '1px',
  height: '1px',
  overflow: 'hidden',
  clip: 'rect(0 0 0 0)',
  whiteSpace: 'nowrap',
} as const;

/** "2 → 5" for the eye and "2 to 5" for a screen reader; a missing side is "—". */
export function Change({ from, to }: { from: number | null; to: number | null }) {
  const show = (value: number | null) => (value === null ? '—' : String(value));
  return (
    <span>
      <span aria-hidden>{`${show(from)} → ${show(to)}`}</span>
      <Box component="span" sx={hidden}>{`${show(from)} to ${show(to)}`}</Box>
    </span>
  );
}
