import { Alert, Grid, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { readinessQuery } from '../api/readinessApi';
import { executionStatusQuery } from '../api/statusApi';
import { overviewQuery } from '../api/viewsApi';
import { AttestationForm } from './AttestationForm';
import { ConfirmSection } from './ConfirmSection';
import { MorningChecklist } from './MorningChecklist';
import { ReadinessCard } from './ReadinessCard';
import { RunbookSteps } from './RunbookSteps';
import { TodayAccount } from './TodayAccount';

/** Why nothing is running: the service, the environment and each account's gate, as the status says them. */
function OffNotice() {
  const { data: status } = useQuery(executionStatusQuery);
  const { data: overview } = useQuery(overviewQuery());
  const off = status && (status.environment === 'none' || overview?.executionEnabled !== true);
  if (!off) return null;
  return (
    <Alert severity="info" role="status" sx={{ mb: 2 }}>
      <strong>Execution is off</strong>
      <ul style={{ margin: 0, paddingLeft: '1.2em' }}>
        <li>Service: {status.service.state}</li>
        <li>Environment: {status.environment}</li>
        <li>Execution switch: {overview?.executionEnabled === true ? 'on' : 'off'}</li>
        {status.accounts.map((a) => (
          <li key={a.accountId}>
            Account gate ({a.mode}): {a.gate.reason}
          </li>
        ))}
      </ul>
    </Alert>
  );
}

/** Today: the mode and caps per account, what waits for the owner, readiness and the morning checklist. */
export function TodayTab() {
  const readiness = useQuery(readinessQuery);
  const overview = useQuery(overviewQuery());
  return (
    <>
      <OffNotice />
      <Typography variant="body2" sx={{ color: 'text.secondary', mb: 2 }}>
        Notifications stay in this app: if the browser is closed nothing reaches you.
      </Typography>
      {overview.isPending && <LoadingState label="Loading today" />}
      {overview.error && <ErrorState error={overview.error} />}
      <Stack sx={{ gap: 2, mb: 3 }}>
        {overview.data?.accounts.map((a) => (
          <TodayAccount key={a.accountId} account={a} />
        ))}
      </Stack>
      <ConfirmSection />
      {readiness.isPending && <LoadingState label="Loading readiness" />}
      {readiness.error && <ErrorState error={readiness.error} />}
      {readiness.data?.accounts.length === 0 && (
        <Alert severity="info" role="status">
          No account is set up for execution.
        </Alert>
      )}
      {readiness.data?.accounts.map((a) => (
        <Stack key={a.accountId} sx={{ gap: 2, mb: 3 }}>
          <Grid container spacing={2}>
            <Grid size={{ xs: 12, lg: 6 }}>
              <ReadinessCard account={a} />
            </Grid>
            <Grid size={{ xs: 12, lg: 6 }}>
              <MorningChecklist account={a} />
            </Grid>
            <Grid size={{ xs: 12, lg: 6 }}>
              <AttestationForm account={a} />
            </Grid>
            <Grid size={{ xs: 12, lg: 6 }}>
              <RunbookSteps />
            </Grid>
          </Grid>
        </Stack>
      ))}
    </>
  );
}
