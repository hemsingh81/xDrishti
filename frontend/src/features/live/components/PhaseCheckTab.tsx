import { Alert, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ApiError } from '@/shared/api';
import { ErrorState, LoadingState } from '@/shared/ui';
import { phaseCheckQuery } from '../api/phaseCheckQueries';
import { PhaseDetails } from './PhaseDetails';
import { PhaseVerdict } from './PhaseVerdict';

const note =
  'Read only: this tab judges the small-live phase from stored records. It changes no setting, moves no ramp step and places no order.';

/** The Phase check tab of the Live screen (spec P11-06): the exit call, each criterion, the weeks, the blockers and the limits. */
export function PhaseCheckTab() {
  const q = useQuery(phaseCheckQuery);
  if (q.isPending) return <LoadingState label="Loading the phase check…" />;
  if (q.isError) {
    if (q.error instanceof ApiError && q.error.status === 404) {
      return <Typography>No account is in manual-live mode, so there is nothing to check.</Typography>;
    }
    return <ErrorState error={q.error} />;
  }
  const { report } = q.data;
  return (
    <Stack sx={{ gap: 2 }}>
      <Alert severity="info">{note}</Alert>
      {q.data.state === 'empty' ? (
        <Typography>No live session yet: nothing to check</Typography>
      ) : (
        <>
          <section aria-label="Exit">
            <PhaseVerdict verdict={report.exitVerdict} />
            <Typography variant="h3" component="h2">
              {report.exitCriterion}
            </Typography>
            <Typography>{report.recommendation}</Typography>
            <Typography variant="body2">
              Through {report.through}; {report.sessions} sessions in {report.elapsedDays} days
            </Typography>
          </section>
          <section aria-label="Criteria">
            <Typography variant="h3" component="h2">
              Criteria
            </Typography>
            <ul>
              {report.criteria.map((c) => (
                <li key={c.code}>
                  <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
                    <strong>{c.code}</strong>
                    <span>{c.name}</span>
                    <PhaseVerdict verdict={c.verdict} />
                  </Stack>
                  <Typography variant="body2">{c.detail}</Typography>
                  {c.caveat && <Typography variant="body2">{c.caveat}</Typography>}
                </li>
              ))}
            </ul>
          </section>
          <PhaseDetails report={report} />
        </>
      )}
    </Stack>
  );
}
