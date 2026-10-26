import { Box, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { touchTarget } from '@/theme/tokens';

/** One "term: value" pair of a description list (the parent is a `dl`). */
export function TermRow({ term, children }: { term: string; children: ReactNode }) {
  return (
    <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
      <Typography component="dt" variant="body2" color="text.secondary">
        {term}:
      </Typography>
      <Typography
        component="dd"
        variant="body2"
        sx={{
          m: 0,
          overflowWrap: 'anywhere',
          '& > a': {
            display: 'inline-block',
            py: '4px',
            [`@media (max-width:${touchTarget.phoneMaxWidth}px)`]: { py: '14px' },
          },
        }}
      >
        {children}
      </Typography>
    </Box>
  );
}
