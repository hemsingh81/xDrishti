import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import { Card, CardContent, IconButton, Stack, Typography } from '@mui/material';
import type { UseQueryResult } from '@tanstack/react-query';
import { useId } from 'react';
import type { HoldingGroups } from '@/shared/api';
import { useChartsCollapsed } from '../hooks/useChartsCollapsed';
import type { GroupBy } from '../lib/filters';
import { AllocationCharts } from './AllocationCharts';

/**
 * The Charts card above the groups: the allocation donut and, for Account and Sector, the stacked bar of the other
 * dimension. Collapsible; the choice is kept for the browser session. It reads the groups the table already loaded, so
 * the charts follow the same filters; collapsed, it draws nothing and loads no chart code.
 */
export function AllocationCard({ by, query }: { by: GroupBy; query: UseQueryResult<HoldingGroups> }) {
  const [collapsed, toggle] = useChartsCollapsed();
  const titleId = useId();
  const bodyId = useId();
  return (
    <Card component="section" aria-labelledby={titleId} sx={{ mb: 2, flexShrink: 0 }}>
      <CardContent>
        <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', gap: 1 }}>
          <Typography id={titleId} variant="h2" component="h2">
            Charts
          </Typography>
          <IconButton
            aria-label={collapsed ? 'Show charts' : 'Hide charts'}
            aria-expanded={!collapsed}
            aria-controls={collapsed ? undefined : bodyId}
            onClick={toggle}
            edge="end"
          >
            {collapsed ? <ExpandMoreIcon /> : <ExpandLessIcon />}
          </IconButton>
        </Stack>
        {!collapsed && (
          <div id={bodyId}>
            {query.isError && query.data === undefined ? (
              <Typography color="text.secondary" sx={{ py: 2 }}>
                The charts need the groups below, which could not be loaded.
              </Typography>
            ) : (
              <AllocationCharts
                key={by}
                by={by}
                data={query.data}
                loading={query.isPending}
                busy={query.isPlaceholderData}
              />
            )}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
