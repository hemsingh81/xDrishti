import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatAsOf } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { hypothesisQuery } from '../api/learningQueries';
import { moveNote } from '../lib/roleText';
import { DrawerSection, RecordDrawer, StepList } from './RecordDrawer';
import { TermRow } from './TermRow';

/** One hypothesis with its history. The states it may move to are text; the move is not made here. */
export function HypothesisDrawer({ id, onClose }: { id: number; onClose: () => void }) {
  const query = useQuery(hypothesisQuery(id));
  const h = query.data;
  return (
    <RecordDrawer title={`Hypothesis #${id}`} closeLabel="Close hypothesis" onClose={onClose}>
      {query.isPending && <LoadingState label="Loading the hypothesis…" />}
      {query.isError && <ErrorState error={query.error} />}
      {h && (
        <>
          <Typography sx={{ overflowWrap: 'anywhere' }}>{h.rationale}</Typography>
          <Box component="dl" sx={{ m: 0, display: 'grid', gap: 0.5 }}>
            <TermRow term="State">{h.state}</TermRow>
            <TermRow term="Source">{h.source}</TermRow>
            <TermRow term="Knob">
              {h.knobName} ({h.knobKind}): {h.baselineValue} to {h.candidateValue}
            </TermRow>
            <TermRow term="Opened">{formatAsOf(h.createdAt)}</TermRow>
          </Box>
          <DrawerSection title="History">
            <StepList label="Steps of this hypothesis" steps={h.history} formatAt={formatAsOf} />
          </DrawerSection>
          <Typography variant="body2">
            May move to: {h.nextStates.length > 0 ? h.nextStates.join(', ') : 'nowhere'}. {moveNote('hypothesis')}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            {h.note}
          </Typography>
        </>
      )}
    </RecordDrawer>
  );
}
