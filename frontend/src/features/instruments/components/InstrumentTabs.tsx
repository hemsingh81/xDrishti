import { Box, Tab, Tabs } from '@mui/material';
import type { InstrumentFacets } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import type { InstrumentTab } from '../lib/filters';

import { panelId, tabId } from '../lib/tabIds';

interface InstrumentTabsProps {
  value: InstrumentTab;
  /** Undefined while loading or when the counts could not be loaded; the tabs still work. */
  facets: InstrumentFacets | undefined;
  onChange: (tab: InstrumentTab) => void;
}

/** Master list · Tracked. The tab is in the URL (`?tab=tracked`), so a view can be shared and Back works. */
export function InstrumentTabs({ value, facets, onChange }: InstrumentTabsProps) {
  return (
    <Box sx={{ borderBottom: 1, borderColor: 'divider', mb: 2, flexShrink: 0 }}>
      <Tabs
        value={value}
        aria-label="Instrument lists"
        variant="scrollable"
        scrollButtons={false}
        onChange={(_event, next: InstrumentTab) => {
          onChange(next);
        }}
      >
        <Tab
          id={tabId('master')}
          value="master"
          label={facets ? `Master list ${formatNumber(facets.total)}` : 'Master list'}
          aria-controls={panelId}
        />
        <Tab
          id={tabId('tracked')}
          value="tracked"
          label={facets ? `Tracked ${formatNumber(facets.tracked)}` : 'Tracked'}
          aria-controls={panelId}
        />
      </Tabs>
    </Box>
  );
}
