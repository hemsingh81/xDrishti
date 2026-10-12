import { Typography } from '@mui/material';
import type { ReactNode } from 'react';

/** The small grey name of a ribbon item ("Instruments", "Covered"). */
export function PillLabel({ children }: { children: ReactNode }) {
  return (
    <Typography component="span" variant="caption" color="text.secondary" sx={{ fontWeight: 600 }}>
      {children}
    </Typography>
  );
}
