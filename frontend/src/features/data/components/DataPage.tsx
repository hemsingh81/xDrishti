import { Alert, Box } from '@mui/material';
import { useRef } from 'react';
import { LoadingState } from '@/shared/ui';
import { useActionsView } from '../hooks/useActionsView';
import { useCoverageFilters } from '../hooks/useCoverageFilters';
import { useDataTab } from '../hooks/useDataTab';
import { useDataWatch } from '../hooks/useDataWatch';
import { useGapsView } from '../hooks/useGapsView';
import { useSectionTab } from '../hooks/useSectionTab';
import { problemText, startErrorText } from '../lib/errors';
import { ActionsPanel } from './ActionsPanel';
import { AttentionLine } from './AttentionLine';
import { BarsDrawer } from './BarsDrawer';
import { CalendarPanel } from './CalendarPanel';
import { CoveragePanel } from './CoveragePanel';
import { DataHeader } from './DataHeader';
import { DataTabPanel } from './DataTabPanel';
import { DataTabs } from './DataTabs';
import { EmptyData } from './EmptyData';
import { GapsPanel } from './GapsPanel';
import { PipelineStrip } from './PipelineStrip';
import { QueryAlert } from './QueryAlert';
import { RunsPanel } from './RunsPanel';
import { StatusRibbon } from './StatusRibbon';
import { UpdateData } from './UpdateData';

/**
 * The Data & import screen, one screen of information: a slim header with Update data, the status ribbon (last tick,
 * instruments, bars, period, Data account, automatic update), the pipeline strip (CSV files, Dhan Data API,
 * aggregates, each with a details popover and live progress), a line for what needs the owner, and the tabs Coverage,
 * Calendar, Corporate actions, Gaps and Runs. From `md` the open tab fills the rest of the viewport and its list
 * scrolls inside. The tab, the lists' filters and the open instrument are in the URL.
 */
export function DataPage() {
  return <DataContent />;
}

function DataContent() {
  const { overview, busy, startJob, startError, dismissStartError } = useDataWatch();
  const filterState = useCoverageFilters();
  const tabState = useDataTab();
  const actionsView = useActionsView();
  const gapsView = useGapsView();
  const panel = useRef<HTMLDivElement>(null);
  const data = overview.data;
  // The bell links here with ?section=actions.
  useSectionTab(panel, data !== undefined);
  // Nothing stored, nothing tracked without bars, and no update running to fill it.
  const nothingStored = data?.bars.totalBars === 0 && data.bars.instrumentsEmpty === 0;
  const { tab } = tabState;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: 0, height: { md: '100%' } }}>
      <DataHeader actions={<UpdateData busy={busy} disabled={!data} onStart={startJob} />} />
      {startError ? (
        <Alert severity="error" onClose={dismissStartError} sx={{ mb: 1.5 }}>
          {startErrorText(startError)}
        </Alert>
      ) : null}
      {overview.isPending && <LoadingState label="Loading data overview" />}
      {overview.isError && !data && (
        <QueryAlert
          message={problemText(overview.error, 'The data overview could not be loaded.')}
          onRetry={() => {
            void overview.refetch();
          }}
        />
      )}
      {data && (
        <>
          <StatusRibbon overview={data} />
          <PipelineStrip
            overview={data}
            busy={busy}
            onStart={startJob}
            onViewGaps={() => {
              tabState.open({ tab: 'gaps', gaps: data.gaps.open > 0 ? 'open' : 'all' });
            }}
          />
          <AttentionLine overview={data} />
          <DataTabs
            value={tab}
            overview={data}
            onChange={(next) => {
              tabState.open({ tab: next });
            }}
          />
          <DataTabPanel tab={tab} ref={panel}>
            {tab === 'coverage' && nothingStored && data.runningJob === null && <EmptyData />}
            {tab === 'coverage' && !nothingStored && <CoveragePanel filterState={filterState} bars={data.bars} />}
            {tab === 'calendar' && <CalendarPanel />}
            {tab === 'actions' && <ActionsPanel state={actionsView} pending={data.pendingCorporateActions} />}
            {tab === 'gaps' && <GapsPanel state={gapsView} counts={data.gaps} />}
            {tab === 'runs' && <RunsPanel />}
          </DataTabPanel>
        </>
      )}
      <BarsDrawer
        id={filterState.openId}
        timeframe={filterState.timeframe}
        onTimeframeChange={filterState.setTimeframe}
        adjusted={filterState.adjusted}
        onAdjustedChange={filterState.setAdjusted}
        onClose={() => {
          filterState.open(null);
        }}
      />
    </Box>
  );
}
