import { Box, Button, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatCount } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { learningCheckQuery } from '../api/learningQueries';
import { CriteriaList } from './CriteriaList';
import { Panel } from './Panel';

const title = 'P6 phase check (learning)';

/**
 * The P6 (learning) phase check, read from stored records. It re-reads two runs and its cost on the owner's data is not
 * measured, so it is read only when the owner presses the button, and the elapsed milliseconds are printed with the result.
 * It is not combined with the P7 check into one score.
 */
export function LearningCheckPanel() {
  // A read, not a write: `refetch` only asks the server again for the same GET.
  const query = useQuery({ ...learningCheckQuery, enabled: false });
  const reading = query.isFetching;
  return (
    <Panel
      title={title}
      actions={
        <Button
          variant="outlined"
          disabled={reading}
          onClick={() => {
            void query.refetch();
          }}
          sx={{ minHeight: 44 }}
        >
          {query.data ? 'Read the P6 check again' : 'Read the P6 check'}
        </Button>
      }
    >
      {reading && !query.data && <LoadingState label="Reading the P6 check…" />}
      {query.isError && <ErrorState error={query.error} />}
      {!query.data && !reading && !query.isError && (
        <Typography variant="body2" color="text.secondary">
          Not read yet. The check re-reads two stored runs and can take seconds; press the button to read it.
        </Typography>
      )}
      {query.data && (
        <Box sx={{ display: 'grid', gap: 1.5 }}>
          <CriteriaList label="P6 criteria" criteria={query.data.criteria} />
          <Typography variant="subtitle2">Limits</Typography>
          <Box component="ul" aria-label="P6 limits" sx={{ m: 0, pl: 2.5 }}>
            {query.data.limits.map((limit, index) => (
              <li key={index}>{limit}</li>
            ))}
          </Box>
          <Typography variant="caption" color="text.secondary">
            Read in {formatCount(query.data.elapsedMs)} ms.
          </Typography>
        </Box>
      )}
    </Panel>
  );
}
