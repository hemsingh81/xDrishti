import { Box, Typography } from '@mui/material';
import { formatNumber } from '@/shared/lib/format';

/** One figure of a `dl` of counts: its label above and the number below. */
export function Count({ label, value }: { label: string; value: number }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary" component="dt">
        {label}
      </Typography>
      <Typography component="dd" className="numeric" sx={{ m: 0, fontWeight: 600 }}>
        {formatNumber(value)}
      </Typography>
    </Box>
  );
}
