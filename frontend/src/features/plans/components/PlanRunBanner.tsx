import { Alert } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatDate, formatIstTime } from '@/shared/lib/format';
import { planRunsQuery, type Plan } from '../api/planQueries';
import { unpublishedRun } from '../lib/unpublishedRun';

/** "No plan was published for <date>: the <gate> gate refused (...): <reason>", from the recorded attempt. Says nothing when the attempts cannot be read or none failed. */
export function PlanRunBanner({ plans }: { plans: readonly Pick<Plan, 'forDate'>[] }) {
  const runs = useQuery(planRunsQuery);
  const run = runs.data ? unpublishedRun(runs.data, plans) : null;
  if (!run) return null;
  const who = run.manual ? ', started manually' : '';
  return (
    <Alert severity="warning" sx={{ mb: 2 }}>
      No plan was published for {formatDate(run.forDate)}: the {run.failedGate ?? 'pipeline'} gate refused (attempt{' '}
      {run.attempt} at {formatIstTime(run.finishedAt)} IST{who}): {run.reason}
    </Alert>
  );
}
