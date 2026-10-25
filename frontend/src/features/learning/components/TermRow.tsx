import { Box, Typography } from '@mui/material';
import type { ReactNode } from 'react';

/** One "term: value" pair of a description list (the parent is a `dl`). */
export function TermRow({ term, children }: { term: string; children: ReactNode }) {
  return (
    <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
      <Typography component="dt" variant="body2" color="text.secondary">
        {term}:
      </Typography>
      <Typography component="dd" variant="body2" sx={{ m: 0, overflowWrap: 'anywhere' }}>
        {children}
      </Typography>
    </Box>
  );
}
