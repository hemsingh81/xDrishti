import { Alert, Box } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { LoadingState } from '@/shared/ui';
import { ToastProvider } from '@/shared/ui/ToastProvider';
import { overviewQuery } from '../api/qualityQueries';
import { useQualityFilters } from '../hooks/useQualityFilters';
import { useQualityWatch } from '../hooks/useQualityWatch';
import { problemText, startErrorText } from '../lib/errors';
import { CorporateActionsAlert } from './CorporateActionsAlert';
import { FixIssuesDialog } from './FixIssuesDialog';
import { InspectDrawer } from './InspectDrawer';
import { InstrumentsCard } from './InstrumentsCard';
import { JobProgress } from './JobProgress';
import { QualityHeader } from './QualityHeader';
import { QueryAlert } from './QueryAlert';
import { RunHistoryDrawer } from './RunHistoryDrawer';
import { SummaryRibbon } from './SummaryRibbon';

/**
 * The Data quality screen: whether tonight's data can be trusted, on one screen. From `md` it fills the viewport under the
 * top bar: a slim header (title, last run, Run history, Run checks), the summary ribbon (score, result chips that filter
 * the table, the gate), one-line notices only when something needs saying, and the instruments table taking the rest of
 * the height. A row opens the Inspect drawer with every check and the buttons that fix an instrument; the run history is
 * a drawer too. Table state and the open instrument are in the URL.
 */
export function QualityPage() {
  return (
    <ToastProvider>
      <QualityContent />
    </ToastProvider>
  );
}

function QualityContent() {
  const watch = useQualityWatch();
  const filterState = useQualityFilters();
  const overview = useQuery(overviewQuery);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [fixOpen, setFixOpen] = useState(false);
  const data = overview.data;
  const { running, startError, dismissStartError } = watch;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: 0, height: { md: '100%' } }}>
      <QualityHeader
        lastRun={data?.lastRun ?? null}
        busy={watch.busy}
        canRun={watch.overview.data !== undefined}
        historyOpen={historyOpen}
        onRun={() => {
          watch.startJob({ kind: 'run' });
        }}
        onFix={() => {
          setFixOpen(true);
        }}
        onOpenHistory={() => {
          setHistoryOpen(true);
        }}
      />
      {data && (
        <SummaryRibbon
          overview={data}
          status={filterState.filters.status}
          onStatusChange={(status) => {
            filterState.update({ status });
          }}
        />
      )}
      {/* While the drawer is open the error shows in it, next to the buttons that caused it. */}
      {startError && filterState.openId === null ? (
        <Alert severity="error" onClose={dismissStartError} sx={{ mb: 1 }}>
          {startErrorText(startError)}
        </Alert>
      ) : null}
      {running && <JobProgress job={running} inline />}
      <CorporateActionsAlert count={data?.pendingCorporateActions ?? 0} />
      {overview.isPending && <LoadingState label="Loading data quality" />}
      {overview.isError && !data && (
        <QueryAlert
          message={problemText(overview.error, 'The data quality could not be loaded.')}
          onRetry={() => {
            void overview.refetch();
          }}
        />
      )}
      {data && (
        <InstrumentsCard filterState={filterState} neverRun={data.lastRun === null && data.counts.checked === 0} />
      )}
      {data && (
        <FixIssuesDialog
          open={fixOpen}
          data={watch.overview.data ?? null}
          counts={data.counts}
          busy={watch.busy}
          onClose={() => {
            setFixOpen(false);
          }}
          onRun={(dhan) => {
            setFixOpen(false);
            watch.startJob({ kind: 'fixAll', dhan });
          }}
        />
      )}
      <RunHistoryDrawer
        open={historyOpen}
        onClose={() => {
          setHistoryOpen(false);
        }}
      />
      <InspectDrawer
        id={filterState.openId}
        watch={watch}
        onClose={() => {
          filterState.open(null);
        }}
      />
    </Box>
  );
}
