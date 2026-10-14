import type { SvgIconComponent } from '@mui/icons-material';
import { Box, Typography } from '@mui/material';
import { formatNumber } from '@/shared/lib/format';
import { numeric, rem } from '@/theme';

interface SummaryCountProps {
  icon: SvgIconComponent;
  label: string;
  value: number;
  /** The colour of the icon and the number when there is something to see (never the only sign: the label says it). */
  tone?: 'success' | 'info' | 'error' | 'warning';
}

/** One count of the strip: an icon, the number and what it counts ("2 Healthy"). A zero is quiet. */
export function SummaryCount({ icon: Icon, label, value, tone }: SummaryCountProps) {
  const color = tone && value > 0 ? `${tone}.main` : 'text.secondary';
  return (
    <Box component="li" sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
      <Icon aria-hidden sx={{ fontSize: 20, color }} />
      <Typography component="span" sx={{ fontSize: rem(20), fontWeight: 700, lineHeight: 1, color, ...numeric }}>
        {formatNumber(value)}
      </Typography>
      <Typography component="span" variant="body2" color="text.secondary">
        {label}
      </Typography>
    </Box>
  );
}
