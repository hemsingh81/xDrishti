import { Box, ToggleButton, ToggleButtonGroup } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useUrlState } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { cellsQuery, cellWindows, type CellWindow } from '../api/learningQueries';
import { CellDrawer } from './CellDrawer';
import { ChangelogTimeline } from './ChangelogTimeline';
import { Heatmap } from './Heatmap';
import { LifecycleFunnel } from './LifecycleFunnel';
import { NoSnapshot } from './NoSnapshot';
import { Panel } from './Panel';

const windowLabel: Record<CellWindow, string> = { all: 'All', '250d': 'Last 250 days', '60d': 'Last 60 days' };

/** Setups: the cell heatmap with a window selector, the lifecycle funnel, the changelog and the open cell. All view state is in the URL. */
export function SetupsTab() {
  const url = useUrlState();
  const span = url.choice('window', cellWindows, 'all');
  const openCell = url.text('cell');
  const cells = useQuery(cellsQuery(span));

  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 2 }}>
      <Panel
        title="Setups by timeframe"
        actions={
          <ToggleButtonGroup
            exclusive
            size="small"
            aria-label="Window"
            // Full width on a phone, the three choices share the row; their labels wrap rather than overflow.
            sx={{
              width: { xs: '100%', sm: 'auto' },
              '& .MuiToggleButton-root': { flex: { xs: 1, sm: 'none' }, px: { xs: 0.5, sm: 1.5 } },
            }}
            value={span}
            onChange={(_event, next: CellWindow | null) => {
              if (next !== null) url.update({ window: next === 'all' ? null : next });
            }}
          >
            {cellWindows.map((code) => (
              <ToggleButton key={code} value={code} sx={{ minHeight: 44 }}>
                {windowLabel[code]}
              </ToggleButton>
            ))}
          </ToggleButtonGroup>
        }
      >
        {cells.isPending && <LoadingState label="Loading the cells…" />}
        {cells.isError && <ErrorState error={cells.error} />}
        {cells.data?.length === 0 && <NoSnapshot />}
        {cells.data && cells.data.length > 0 && (
          <Heatmap
            cells={cells.data}
            selectedKey={openCell}
            onOpen={(cell) => {
              url.update({ cell });
            }}
          />
        )}
      </Panel>
      <Box
        sx={{
          display: 'grid',
          gap: 2,
          gridTemplateColumns: { xs: 'minmax(0, 1fr)', lg: 'minmax(0, 1fr) minmax(0, 1fr)' },
          alignItems: 'start',
        }}
      >
        <LifecycleFunnel />
        <Panel title="Changelog">
          <ChangelogTimeline label="Changelog, newest first" />
        </Panel>
      </Box>
      {openCell !== null && (
        <CellDrawer
          cellKey={openCell}
          onClose={() => {
            url.update({ cell: null });
          }}
        />
      )}
    </Box>
  );
}
