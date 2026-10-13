import { Box } from '@mui/material';
import type { ActionFigure } from '../../lib/actionFigures';

/** The numbers behind an item as label/value pairs: a description list that wraps (tabular digits). */
export function ActionFigures({ figures }: { figures: readonly ActionFigure[] }) {
  if (figures.length === 0) return null;
  return (
    <Box
      component="dl"
      sx={{
        m: 0,
        mt: 0.75,
        display: 'flex',
        flexWrap: 'wrap',
        columnGap: 2.5,
        rowGap: 0.5,
        '& > div': { display: 'flex', gap: 0.75, alignItems: 'baseline' },
        '& dt': { color: 'text.secondary', typography: 'caption' },
        '& dd': { m: 0, typography: 'body2', fontWeight: 600, fontVariantNumeric: 'tabular-nums' },
      }}
    >
      {figures.map((figure) => (
        <div key={figure.label}>
          <dt>{figure.label}</dt>
          <dd>{figure.value}</dd>
        </div>
      ))}
    </Box>
  );
}
