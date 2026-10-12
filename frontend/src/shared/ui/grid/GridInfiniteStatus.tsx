import { Box, Typography } from '@mui/material';
import { formatNumber } from '@/shared/lib/format';

/** The slim footer of an infinite list: "Showing 100 of 3,204", announced politely as pages arrive. */
export function GridInfiniteStatus({ shown, total, hidden }: { shown: number; total: number; hidden: boolean }) {
  const text = hidden ? '' : total === 0 ? '0 results' : `Showing ${formatNumber(shown)} of ${formatNumber(total)}`;
  return (
    <Box
      sx={{ flexShrink: 0, borderTop: 1, borderColor: 'divider', px: 2, py: 0.5, minHeight: 28, textAlign: 'right' }}
    >
      <Typography
        variant="caption"
        color="text.secondary"
        role="status"
        aria-live="polite"
        sx={{ fontVariantNumeric: 'tabular-nums' }}
      >
        {text}
      </Typography>
    </Box>
  );
}
