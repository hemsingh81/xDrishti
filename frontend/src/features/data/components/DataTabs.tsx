import { Box, Tab, Tabs } from '@mui/material';
import type { DataOverview } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { rem } from '@/theme';
import { dataTabLabels, dataTabs, panelId, tabId, type DataTab } from '../lib/tabs';

interface DataTabsProps {
  value: DataTab;
  overview: Pick<DataOverview, 'calendarProposals' | 'pendingCorporateActions' | 'gaps'>;
  onChange: (tab: DataTab) => void;
}

/** The count a tab carries and what it counts (said in the tab's name too); none for the tabs without one. */
function badgeOf(tab: DataTab, overview: DataTabsProps['overview']): { count: number; what: string } | null {
  const badges: Partial<Record<DataTab, { count: number; what: string }>> = {
    calendar: { count: overview.calendarProposals, what: 'to review' },
    actions: { count: overview.pendingCorporateActions, what: 'suggested' },
    gaps: { count: overview.gaps.open, what: 'open' },
  };
  const badge = badges[tab];
  return badge && badge.count > 0 ? badge : null;
}

/** Coverage · Calendar · Corporate actions · Gaps · Runs, with counts of what waits; scrolls sideways on a phone. */
export function DataTabs({ value, overview, onChange }: DataTabsProps) {
  return (
    <Box sx={{ borderBottom: 1, borderColor: 'divider', mb: 1, flexShrink: 0 }}>
      <Tabs
        value={value}
        aria-label="Data views"
        variant="scrollable"
        scrollButtons={false}
        sx={{ minHeight: 40 }}
        onChange={(_event, next: DataTab) => {
          onChange(next);
        }}
      >
        {dataTabs.map((tab) => {
          const badge = badgeOf(tab, overview);
          return (
            <Tab
              key={tab}
              id={tabId(tab)}
              value={tab}
              aria-controls={panelId(tab)}
              aria-label={badge ? `${dataTabLabels[tab]}, ${formatNumber(badge.count)} ${badge.what}` : undefined}
              sx={{ minHeight: 40, py: 0 }}
              label={
                <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.75 }}>
                  {dataTabLabels[tab]}
                  {badge && (
                    <Box
                      component="span"
                      sx={{
                        minWidth: 20,
                        px: 0.75,
                        borderRadius: 1,
                        bgcolor: 'warning.main',
                        color: 'warning.contrastText',
                        fontSize: rem(12),
                        fontWeight: 700,
                        lineHeight: '20px',
                        textAlign: 'center',
                        fontVariantNumeric: 'tabular-nums',
                      }}
                    >
                      {formatNumber(badge.count)}
                    </Box>
                  )}
                </Box>
              }
            />
          );
        })}
      </Tabs>
    </Box>
  );
}
