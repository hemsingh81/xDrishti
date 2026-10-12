import { Box } from '@mui/material';
import type { ReactNode } from 'react';
import { PillLabel } from './RibbonPill';
import { pillSx } from './ribbonStyle';

interface RibbonStatProps {
  label: string;
  /** The figure; it can carry a `title` with the full number. */
  children: ReactNode;
  /** Words after the figure, e.g. "12 behind" with their own icon and colour. */
  note?: ReactNode;
}

/** A plain ribbon item: its name and a figure (and what is wrong with it, if anything). */
export function RibbonStat({ label, children, note }: RibbonStatProps) {
  return (
    <Box sx={pillSx()}>
      <PillLabel>{label}</PillLabel>
      <Box component="span" sx={{ fontWeight: 700 }}>
        {children}
      </Box>
      {note}
    </Box>
  );
}
