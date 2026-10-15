import { Box, Typography } from '@mui/material';
import { formatNumber } from '@/shared/lib/format';

/** One count of a file as a small bordered tile (a `dt` label and a `dd` number). */
export function SummaryTile({ label, value }: { label: string; value: number }) {
  return (
    <Box sx={{ minWidth: 96, px: 1.5, py: 1, border: 1, borderColor: 'divider', borderRadius: 1 }}>
      <Typography variant="caption" color="text.secondary" component="dt">
        {label}
      </Typography>
      <Typography component="dd" className="numeric" sx={{ m: 0, fontWeight: 600 }}>
        {formatNumber(value)}
      </Typography>
    </Box>
  );
}
