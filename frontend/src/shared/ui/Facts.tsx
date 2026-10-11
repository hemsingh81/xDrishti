import { Box } from '@mui/material';
import type { ReactNode } from 'react';

export interface Fact {
  label: string;
  value: ReactNode;
}

/** Label/value pairs as a description list (semantic, and it wraps on narrow screens). */
export function Facts({ items }: { items: Fact[] }) {
  return (
    <Box
      component="dl"
      sx={{
        m: 0,
        display: 'grid',
        gridTemplateColumns: { xs: '1fr', sm: 'minmax(120px, 190px) minmax(0, 1fr)' },
        columnGap: 2,
        rowGap: { xs: 0.25, sm: 1.25 },
        '& dt': { color: 'text.secondary', fontWeight: 600, mt: { xs: 1, sm: 0 } },
        '& dd': { m: 0, overflowWrap: 'anywhere' },
      }}
    >
      {items.map((item) => (
        <Box key={item.label} sx={{ display: 'contents' }}>
          <dt>{item.label}</dt>
          <dd>{item.value}</dd>
        </Box>
      ))}
    </Box>
  );
}
