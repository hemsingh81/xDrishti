import { Button, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { formatDateTime } from '@/shared/lib/format';
import { rampQuery } from '../api/rampQueries';
import { kindLabels, pct, stepNames } from '../lib/rampLabels';

/** The step card and the history of one account (read only); the button only asks the tab to open the dialog. */
export function RampAccount({ accountId, onSetStep }: { accountId: string; onSetStep: (step: number | null) => void }) {
  const q = useQuery(rampQuery(accountId));
  if (q.isPending) return <LoadingState label="Loading the step…" />;
  if (q.isError) return <ErrorState error={q.error} />;
  const r = q.data;
  const canMove = r.writesEnabled && (r.step === null || r.step > 1);
  return (
    <Stack sx={{ gap: 1.5 }}>
      <section aria-label="Ramp step">
        <Typography variant="h3" component="h2">
          {r.step === null ? 'No step set' : `Step ${stepNames[r.step] ?? String(r.step)}`}
        </Typography>
        <Typography>
          {r.step === null
            ? 'No step is set, so no live ticket is sized for this account.'
            : `Intended risk ${pct(r.intendedRiskPct)} per trade, live risk ${pct(r.liveRiskPct)}.`}
        </Typography>
        {r.hold && <Typography>Raising is on hold{r.holdReason ? `: ${r.holdReason}` : ''}.</Typography>}
        <Typography>{r.eligible ? 'Eligible to move up.' : 'Not eligible to move up.'}</Typography>
      </section>
      <div>
        <Button
          variant="contained"
          disabled={!canMove}
          onClick={() => {
            onSetStep(r.step);
          }}
        >
          {r.step === null ? 'Set the first step' : 'Lower the step'}
        </Button>
        {!r.writesEnabled && (
          <Typography variant="body2" color="text.secondary">
            Writing is switched off in this deployment (<code>Live__Writes__Enabled</code> is false).
          </Typography>
        )}
        {r.writesEnabled && r.step === 1 && (
          <Typography variant="body2" color="text.secondary">
            S1 is the lowest step.
          </Typography>
        )}
      </div>
      {r.history.length === 0 ? (
        <Typography>No step event yet.</Typography>
      ) : (
        <table aria-label="Ramp history">
          <thead>
            <tr>
              <th scope="col">When</th>
              <th scope="col">What</th>
              <th scope="col">Step</th>
              <th scope="col">Who</th>
              <th scope="col">Reason</th>
            </tr>
          </thead>
          <tbody>
            {r.history.map((e) => (
              <tr key={e.id}>
                <td>{formatDateTime(e.createdAt)}</td>
                <td>{kindLabels[e.kind] ?? e.kind}</td>
                <td>{`${e.fromStep === null ? 'none' : String(e.fromStep)} to ${String(e.toStep)}`}</td>
                <td>{e.actor}</td>
                <td>{e.reason}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Stack>
  );
}
