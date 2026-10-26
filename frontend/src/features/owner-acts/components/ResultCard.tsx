import { Alert, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router';
import { Facts, ErrorState, LoadingState } from '@/shared/ui';
import { judgementQuery, type Job } from '../api/experimentApi';

/** The server's closing sentence; also kept here so a card never ends without it. */
export const closing = 'Nothing was changed. Applying a change is your own separate act.';

const outcomeWords: Record<string, string> = {
  Better: 'Better',
  NotBetter: 'Not better',
  Inconclusive: 'Inconclusive',
  CheaperAssumption: 'Cheaper assumption',
};

function Judgement({ id }: { id: number }) {
  const query = useQuery(judgementQuery(id));
  if (query.isPending) return <LoadingState label="Loading the judgement…" />;
  if (query.isError) return <ErrorState error={query.error} />;
  const e = query.data;
  return (
    <Stack spacing={1}>
      <Facts
        items={[
          { label: 'Outcome', value: outcomeWords[e.outcome] ?? e.outcome },
          { label: 'Folds better', value: `${e.foldsBetter} of ${e.usedFolds} used (${e.foldsNeeded} needed)` },
          { label: 'Required improvement', value: `${e.requiredImprovementR} R per trade` },
          {
            label: 'Total R after the gap',
            value: `Baseline ${e.baselineTotalR}, candidate ${e.candidateTotalR} (gap ${e.expectedLiveGapR} R per trade)`,
          },
          { label: 'Trials', value: `${e.trials} in the last 30 days, this one included` },
        ]}
      />
      {e.reasons.length > 0 && (
        <ul style={{ margin: 0, paddingLeft: '1.25rem' }} aria-label="Reasons">
          {e.reasons.map((r) => (
            <li key={r}>{r}</li>
          ))}
        </ul>
      )}
      <RouterLink to={`/learning?tab=experiments&experiment=${e.id}`}>Open experiment #{e.id} in Learning</RouterLink>
    </Stack>
  );
}

/** What a finished job says. A job that judged nothing says so; every card ends with the closing sentence. */
export function ResultCard({ job }: { job: Job }) {
  const experimentId = job.state === 'succeeded' ? job.experimentId : null;
  return (
    <Stack spacing={1.5} component="article" aria-label="Result">
      {experimentId !== null ? (
        <Judgement id={experimentId} />
      ) : (
        <Alert severity="warning">{job.errorText ?? `The job ${job.state} before it judged anything.`}</Alert>
      )}
      {job.timing && <Typography>{job.timing.text}</Typography>}
      {job.limits
        .filter((text) => text !== closing)
        .map((text) => (
          <Typography key={text} variant="body2" color="text.secondary">
            {text}
          </Typography>
        ))}
      <Typography sx={{ fontWeight: 600 }}>{closing}</Typography>
    </Stack>
  );
}
