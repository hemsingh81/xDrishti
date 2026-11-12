import { Alert, Box, Stack } from '@mui/material';
import { visuallyHidden } from '@mui/utils';
import { useQuery } from '@tanstack/react-query';
import { LoadingState } from '@/shared/ui';
import { modesQuery } from '../api/viewsApi';
import { PhaseCheckPanel } from './PhaseCheckPanel';
import { LadderCard } from './LadderCard';
import { RetryError } from './RetryError';
import { DrillCards } from './DrillCards';
import { ExecutionSwitchCard } from './ExecutionSwitchCard';

/** Modes: the ladder per account, promotion and demotion, the switches and the limits. */
export function ModesTab() {
  const { data, error, isPending, refetch } = useQuery(modesQuery);
  return (
    <Stack sx={{ gap: 3 }}>
      {/* Hidden words, and a spacer below the switch where the ladder lands: a 124 px spinner on top moved everything up (shift 0.56). */}
      {isPending && (
        <Box sx={visuallyHidden}>
          <LoadingState label="Loading modes" />
        </Box>
      )}
      {error && <RetryError error={error} onRetry={() => void refetch()} />}
      <ExecutionSwitchCard />
      {isPending && <Box aria-hidden sx={{ minHeight: { xs: 584, md: 300 } }} />}
      {data?.accounts.length === 0 && (
        <Alert severity="info" role="status">
          No account is set up for execution.
        </Alert>
      )}
      {data?.accounts.map((a) => (
        <LadderCard key={a.accountId} account={a} />
      ))}
      {data && data.accounts.length > 0 && <DrillCards accounts={data.accounts} />}
      <PhaseCheckPanel />
    </Stack>
  );
}
