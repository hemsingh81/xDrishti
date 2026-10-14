import { Box, Card, CardContent, Typography } from '@mui/material';
import type { SvgIconComponent } from '@mui/icons-material';
import type { ReactNode } from 'react';
import { numeric, rem, type IconHueName } from '@/theme';
import { IconChip } from './IconChip';

interface StatTileProps {
  label: string;
  value: ReactNode;
  hint?: ReactNode;
  tone?: 'default' | 'good' | 'bad';
  /** Optional icon chip, top right; needs `hue` too. */
  icon?: SvgIconComponent;
  hue?: IconHueName;
}

const toneColor = { default: 'text.primary', good: 'success.main', bad: 'error.main' } as const;

/** Small KPI tile (label, big value, optional hint). A grid can reserve room for a wrapping label with `--stat-tile-label-min`. */
export function StatTile({ label, value, hint, tone = 'default', icon, hue }: StatTileProps) {
  return (
    <Card sx={{ position: 'relative' }}>
      {icon && hue && (
        <Box sx={{ position: 'absolute', top: 12, right: 12 }}>
          <IconChip icon={icon} hue={hue} size="md" />
        </Box>
      )}
      <CardContent>
        {/* The prototype's `.tile`: 12 px label (room for the icon chip), 22 px value, 12 px hint. */}
        <Typography
          variant="caption"
          component="p"
          color="text.secondary"
          sx={{ m: 0, fontWeight: 600, pr: icon && hue ? '40px' : 0, minHeight: 'var(--stat-tile-label-min, 0)' }}
        >
          {label}
        </Typography>
        <Typography
          component="div"
          sx={{
            fontSize: rem(22),
            fontWeight: 750,
            letterSpacing: '-0.02em',
            mt: 0.5,
            color: toneColor[tone],
            ...numeric,
          }}
        >
          {value}
        </Typography>
        {hint && (
          <Typography component="div" variant="caption" color="text.secondary">
            {hint}
          </Typography>
        )}
      </CardContent>
    </Card>
  );
}
