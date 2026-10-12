import { Box } from '@mui/material';
import type { ReactNode, Ref } from 'react';
import { panelId, tabId, type DataTab } from '../lib/tabs';

interface DataTabPanelProps {
  tab: DataTab;
  /** Where a link that names a section (the bell's) moves the focus. */
  ref?: Ref<HTMLDivElement>;
  children: ReactNode;
}

/**
 * The open tab's content. From `md` it takes the rest of the viewport (the page does not scroll; a grid inside scrolls
 * itself); on a phone it is a fixed share of the screen, so the grid scrolls inside it and the page scrolls to it.
 */
export function DataTabPanel({ tab, ref, children }: DataTabPanelProps) {
  return (
    <Box
      ref={ref}
      role="tabpanel"
      id={panelId(tab)}
      aria-labelledby={tabId(tab)}
      tabIndex={-1}
      sx={{
        display: 'flex',
        flexDirection: 'column',
        height: { xs: '75dvh', md: 'auto' },
        flex: { md: '1 1 auto' },
        minHeight: { xs: 360, md: 240 },
      }}
    >
      {children}
    </Box>
  );
}
