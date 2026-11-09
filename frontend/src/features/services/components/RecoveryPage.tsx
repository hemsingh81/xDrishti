import RestoreIcon from '@mui/icons-material/RestoreOutlined';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import { Accordion, AccordionDetails, AccordionSummary, Alert, Button, Link, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router';
import { formatDateTime } from '@/shared/lib/format';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { recoveryQuery } from '../api/recoveryApi';
import { recoveryPollMs } from '../lib/polling';
import { recoveryStatus } from '../lib/recoveryStatus';
import { RecoveryCounts } from './RecoveryCounts';
import { RecoveryRetry } from './RecoveryRetry';
import { RecoverySessions } from './RecoverySessions';

const days = 14;

/**
 * What was missed, what was recovered and what is still failing. Three counts, what needs attention, the details
 * collapsed. Nothing runs on load; it is read every 5 seconds only while a pass runs.
 */
export function RecoveryPage() {
  const { data, error, isPending, refetch } = useQuery({
    ...recoveryQuery(days),
    refetchInterval: (query) => (query.state.data?.running ? recoveryPollMs : false),
  });

  const attention = (data?.sessions ?? [])
    .map((s) => ({ ...s, items: s.items.filter((row) => recoveryStatus(row.status).attention) }))
    .filter((s) => s.items.length > 0);
  const canRetry = data?.sessions.some((s) => s.items.some((row) => row.canRetry)) ?? false;

  return (
    <>
      <PageHeader
        title="Recovery"
        icon={RestoreIcon}
        hue="services"
        description={`Days a service missed in the last ${String(days)} days, and what became of them.`}
        actions={
          <>
            <Link component={RouterLink} to="/services">
              Back to Services
            </Link>
            {canRetry && <RecoveryRetry disabled={data?.running ?? false} />}
          </>
        }
      />
      {isPending && <LoadingState label="Loading recovery status" />}
      {error && (
        <>
          <ErrorState error={error} />
          <Button
            onClick={() => {
              void refetch();
            }}
          >
            Retry
          </Button>
        </>
      )}
      {data && (
        <>
          <Typography color="text.secondary" sx={{ mb: 1.5 }}>
            Last checked {formatDateTime(data.checkedAt)}.
            {data.latestPass &&
              ` Latest pass: ${data.latestPass.trigger}, ${data.latestPass.status}, started ${formatDateTime(data.latestPass.startedAt)}.`}
            {data.running && ' A recovery pass is running now.'}
          </Typography>
          <RecoveryCounts counts={data.counts} />
          {data.truncated && (
            <Alert severity="info" sx={{ mb: 2 }}>
              There is more than can be shown here; these are the newest 500.
            </Alert>
          )}
          {data.planUnavailable ? (
            <Alert severity="warning">{data.planUnavailable.reason}</Alert>
          ) : data.sessions.length === 0 ? (
            <Typography>
              Nothing was missed in the last {data.days} days. Last checked {formatDateTime(data.checkedAt)}.
            </Typography>
          ) : (
            <>
              <Typography variant="h2" sx={{ fontSize: '1.125rem', mb: 1 }}>
                Needs attention
              </Typography>
              {attention.length === 0 ? (
                <Typography sx={{ mb: 2 }}>Nothing needs attention.</Typography>
              ) : (
                <RecoverySessions sessions={attention} label="Needs attention" />
              )}
              <Accordion
                disableGutters
                variant="outlined"
                sx={{ mt: 2 }}
                slotProps={{ transition: { unmountOnExit: true } }}
              >
                <AccordionSummary expandIcon={<ExpandMoreIcon />}>All details</AccordionSummary>
                <AccordionDetails>
                  <RecoverySessions sessions={data.sessions} label="All sessions" />
                </AccordionDetails>
              </Accordion>
            </>
          )}
        </>
      )}
    </>
  );
}
