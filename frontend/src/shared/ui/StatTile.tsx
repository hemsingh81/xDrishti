import { Card, CardContent, Typography } from '@mui/material';
import type { ReactNode } from 'react';

interface StatTileProps {
  label: string;
  value: ReactNode;
  hint?: ReactNode;
  tone?: 'default' | 'good' | 'bad';
}

const toneColor = { default: 'text.primary', good: 'success.main', bad: 'error.main' } as const;

/** Small KPI tile (label, big value, optional hint). */
export function StatTile({ label, value, hint, tone = 'default' }: StatTileProps) {
  return (
    <Card>
      <CardContent>
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
            fontVariantNumeric: 'tabular-nums',
          }}
        >
          {value}
        </Typography>
        {hint && (
          <Typography variant="body2" color="text.secondary">
            {hint}
          </Typography>
        )}
      </CardContent>
    </Card>
  );
}
