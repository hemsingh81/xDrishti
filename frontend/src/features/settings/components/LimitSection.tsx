import { Paper, Typography } from '@mui/material';
import type { ReactNode } from 'react';

/** One limit setting on the Limits tab: a titled section with what is in force, its change form and its history. */
export function LimitSection({ id, title, children }: { id: string; title: string; children: ReactNode }) {
  return (
    <Paper
      variant="outlined"
      component="section"
      aria-labelledby={id}
      sx={{ p: 2, display: 'grid', gap: 2, minWidth: 0 }}
    >
      <Typography id={id} variant="h3" component="h2">
        {title}
      </Typography>
      {children}
    </Paper>
  );
}
