import { Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { experimentQuery } from '../api/learningQueries';
import { OutcomeBadge } from './OutcomeBadge';
import { PreformattedText } from './PreformattedText';
import { DrawerSection } from './DrawerSection';
import { RecordDrawer } from './RecordDrawer';

/** One experiment: the stored report (with its honest limits) and the rules used, as text. */
export function ExperimentDrawer({ id, onClose }: { id: number; onClose: () => void }) {
  const query = useQuery(experimentQuery(id));
  const e = query.data;
  return (
    <RecordDrawer
      title={`Experiment #${id}`}
      subtitle={e ? `${e.knobName}: ${e.baselineValue} to ${e.candidateValue}` : undefined}
      closeLabel="Close experiment"
      onClose={onClose}
    >
      {query.isPending && <LoadingState label="Loading the experiment…" />}
      {query.isError && <ErrorState error={query.error} />}
      {e && (
        <>
          <OutcomeBadge name={e.outcome} />
          <DrawerSection title="Report">
            <PreformattedText label="Experiment report" text={e.reportText} />
          </DrawerSection>
          <DrawerSection title="Parameters">
            <PreformattedText label="Rules used" text={e.parameters} />
          </DrawerSection>
          <Typography variant="caption" color="text.secondary">
            Judged on replayed history with modelled fills; the report holds its limits.
          </Typography>
        </>
      )}
    </RecordDrawer>
  );
}
