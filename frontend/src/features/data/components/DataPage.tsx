import StorageIcon from '@mui/icons-material/StorageOutlined';
import { Alert } from '@mui/material';
import { LoadingState, PageHeader } from '@/shared/ui';
import { ToastProvider } from '@/shared/ui/ToastProvider';
import { useCoverageFilters } from '../hooks/useCoverageFilters';
import { useDataWatch } from '../hooks/useDataWatch';
import { problemText, startErrorText } from '../lib/errors';
import { BarsDrawer } from './BarsDrawer';
import { CoverageCard } from './CoverageCard';
import { EmptyData } from './EmptyData';
import { FreshnessStrip } from './FreshnessStrip';
import { PipelineCard } from './PipelineCard';
import { QueryAlert } from './QueryAlert';
import { RecentRuns } from './RecentRuns';
import { UpdateData } from './UpdateData';

/**
 * The Data & import screen: how fresh the stored bars are, the pipeline (CSV files, Dhan Data API, aggregates) with
 * live progress, what is stored per instrument, and the recent runs. Table state and the open instrument are in the URL.
 */
export function DataPage() {
  return (
    <ToastProvider>
      <DataContent />
    </ToastProvider>
  );
}

function DataContent() {
  const { overview, busy, startJob, startError, dismissStartError } = useDataWatch();
  const filterState = useCoverageFilters();
  const data = overview.data;
  // Nothing stored, nothing tracked without bars, and no update running to fill it.
  const nothingStored = data?.bars.totalBars === 0 && data.bars.instrumentsEmpty === 0;

  return (
    <>
      <PageHeader
        icon={StorageIcon}
        hue="data"
        title="Data & import"
        description="Load the CSV folder, fetch what is missing from Dhan, and build every timeframe from the 1-minute bars. One button does all three."
        actions={<UpdateData busy={busy} disabled={!data} onStart={startJob} />}
      />
      {startError ? (
        <Alert severity="error" onClose={dismissStartError} sx={{ mb: 2 }}>
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
          <FreshnessStrip overview={data} />
          <PipelineCard overview={data} />
          {nothingStored && data.runningJob === null && <EmptyData />}
          {!nothingStored && <CoverageCard filterState={filterState} />}
          <RecentRuns />
        </>
      )}
      <BarsDrawer
        id={filterState.openId}
        timeframe={filterState.timeframe}
        onTimeframeChange={filterState.setTimeframe}
        onClose={() => {
          filterState.open(null);
        }}
      />
    </>
  );
}
