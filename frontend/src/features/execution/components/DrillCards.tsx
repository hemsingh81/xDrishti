import { Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { LoadingState } from '@/shared/ui';
import { drillsQuery } from '../api/switchApi';
import { DrillCard } from './DrillCard';
import { RetryError } from './RetryError';

/** One kill-switch drill card per account, from one read of the drills. */
export function DrillCards({ accounts }: { accounts: { accountId: string; name: string }[] }) {
  const { data, error, isPending, refetch } = useQuery(drillsQuery);
  return (
    <Stack sx={{ gap: 2 }}>
      <Typography variant="h2" sx={{ fontSize: 18 }}>
        Kill-switch drill
      </Typography>
      {isPending && <LoadingState label="Loading drills" />}
      {error && <RetryError error={error} onRetry={() => void refetch()} />}
      {data?.drills !== undefined &&
        accounts.map((a) => <DrillCard key={a.accountId} accountId={a.accountId} name={a.name} drills={data} />)}
    </Stack>
  );
}
