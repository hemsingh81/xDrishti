import { Box } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { NonValue, useUrlState } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { experimentsQuery } from '../api/learningQueries';
import { parseId } from '../lib/roleText';
import { ExperimentDrawer } from './ExperimentDrawer';
import { ExperimentTable } from './ExperimentTable';
import { HypothesesSection } from './HypothesesSection';
import { Panel } from './Panel';

/** Experiments (newest 50) and, below them, the hypotheses behind them. Read only. */
export function ExperimentsTab() {
  const url = useUrlState();
  const open = parseId(url.text('experiment'));
  const query = useQuery(experimentsQuery);
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 2 }}>
      <Panel title="Experiments">
        {query.isPending && <LoadingState label="Loading the experiments…" />}
        {query.isError && <ErrorState error={query.error} />}
        {query.data?.length === 0 && (
          <NonValue state={{ kind: 'empty', reason: 'No experiment stored yet; run `xd learn experiment`.' }} />
        )}
        {query.data && query.data.length > 0 && (
          <ExperimentTable
            rows={query.data}
            onOpen={(id) => {
              url.update({ experiment: String(id) });
            }}
          />
        )}
      </Panel>
      <HypothesesSection />
      {open !== null && (
        <ExperimentDrawer
          id={open}
          onClose={() => {
            url.update({ experiment: null });
          }}
        />
      )}
    </Box>
  );
}
