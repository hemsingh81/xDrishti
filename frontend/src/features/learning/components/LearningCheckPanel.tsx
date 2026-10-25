import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatCount } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { learningCheckQuery } from '../api/learningQueries';
import { CriteriaList } from './CriteriaList';
import { Panel } from './Panel';

/**
 * The P6 (learning) phase check, read from stored records. It re-reads two runs, so the elapsed milliseconds are printed. It
 * is not combined with the P7 check into one score.
 */
export function LearningCheckPanel() {
  const query = useQuery(learningCheckQuery);
  if (query.isPending) return <LoadingState label="Loading the P6 check…" />;
  if (query.isError)
    return (
      <Panel title="P6 phase check">
        <ErrorState error={query.error} />
      </Panel>
    );
  const r = query.data;
  return (
    <Panel title="P6 phase check (learning)">
      <Box sx={{ display: 'grid', gap: 1.5 }}>
        <CriteriaList label="P6 criteria" criteria={r.criteria} />
        <Typography variant="subtitle2">Limits</Typography>
        <Box component="ul" aria-label="P6 limits" sx={{ m: 0, pl: 2.5 }}>
          {r.limits.map((limit, index) => (
            <li key={index}>{limit}</li>
          ))}
        </Box>
        <Typography variant="caption" color="text.secondary">
          Read in {formatCount(r.elapsedMs)} ms.
        </Typography>
      </Box>
    </Panel>
  );
}
