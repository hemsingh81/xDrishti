import ChecklistIcon from '@mui/icons-material/ChecklistOutlined';
import { Alert, Grid, Stack } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { readinessQuery } from '../api/readinessApi';
import { AttestationForm } from './AttestationForm';
import { MorningChecklist } from './MorningChecklist';
import { ConfirmSection } from './ConfirmSection';
import { ReadinessCard } from './ReadinessCard';
import { RunbookSteps } from './RunbookSteps';

/** Execution, Today (minimal until P12-08/P12-10): readiness, the morning checklist and the IP attestation. */
export function ExecutionPage() {
  const { data, error, isPending } = useQuery(readinessQuery);
  return (
    <>
      <PageHeader
        title="Execution"
        icon={ChecklistIcon}
        hue="system"
        description="Whether execution can act today, and what is missing. Nothing is ready until a check says so."
      />
      <ConfirmSection />
      {isPending && <LoadingState label="Loading readiness" />}
      {error && <ErrorState error={error} />}
      {data?.accounts.length === 0 && <Alert severity="info">No account is set up for execution.</Alert>}
      {data?.accounts.map((a) => (
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
