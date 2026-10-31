import { Alert, Stack } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { LoadingState } from '@/shared/ui';
import { modesQuery } from '../api/viewsApi';
import { LadderCard } from './LadderCard';
import { RetryError } from './RetryError';
import { SwitchesPanel } from './SwitchesPanel';

/** Modes: the ladder per account, promotion and demotion, the switches and the limits. */
export function ModesTab() {
  const { data, error, isPending, refetch } = useQuery(modesQuery);
  return (
    <Stack sx={{ gap: 3 }}>
      {isPending && <LoadingState label="Loading modes" />}
      {error && <RetryError error={error} onRetry={() => void refetch()} />}
      {data && <SwitchesPanel executionEnabled={data.executionEnabled} />}
      {data?.accounts.length === 0 && (
        <Alert severity="info" role="status">
          No account is set up for execution.
        </Alert>
      )}
      {data?.accounts.map((a) => (
        <LadderCard key={a.accountId} account={a} />
      ))}
    </Stack>
  );
}
