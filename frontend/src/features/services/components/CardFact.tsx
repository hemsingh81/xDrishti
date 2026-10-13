import { Box, Typography } from '@mui/material';
import type { ReactNode } from 'react';

/** A labelled fact on a card: a small caption over the value. */
export function CardFact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Box sx={{ minWidth: 0 }}>
      <Typography variant="overline" color="text.secondary" component="div" sx={{ lineHeight: 1.6 }}>
        {label}
      </Typography>
      {children}
    </Box>
  );
}
