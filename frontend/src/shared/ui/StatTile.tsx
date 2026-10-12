import { Box, Card, CardContent, Typography } from '@mui/material';
import type { SvgIconComponent } from '@mui/icons-material';
import type { ReactNode } from 'react';
import { numeric, type IconHueName } from '@/theme';
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

/** Small KPI tile (label, big value, optional hint). */
export function StatTile({ label, value, hint, tone = 'default', icon, hue }: StatTileProps) {
  return (
    <Card sx={{ position: 'relative' }}>
      {icon && hue && (
        <Box sx={{ position: 'absolute', top: 12, right: 12 }}>
          <IconChip icon={icon} hue={hue} size="md" />
        </Box>
      )}
      <CardContent sx={icon && hue ? { pr: 7.5 } : undefined}>
        <Typography variant="body2" color="text.secondary" sx={{ fontWeight: 600 }}>
          {label}
        </Typography>
        <Typography
          component="div"
          sx={{
            fontSize: '1.4rem',
            fontWeight: 700,
            mt: 0.5,
            color: toneColor[tone],
            ...numeric,
          }}
        >
          {value}
        </Typography>
        {hint && (
          <Typography component="div" variant="body2" color="text.secondary">
            {hint}
          </Typography>
        )}
      </CardContent>
    </Card>
  );
}
