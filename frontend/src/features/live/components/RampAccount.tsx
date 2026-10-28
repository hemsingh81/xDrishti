import { Button, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { formatDateTime } from '@/shared/lib/format';
import { rampQuery } from '../api/rampQueries';
import { kindLabels, pct, stepNames } from '../lib/rampLabels';
import { RampGatesTable } from './RampGatesTable';
import { RampStanding } from './RampStanding';

export type RampAct =
  { kind: 'step'; step: number | null; raise?: boolean } | { kind: 'clear-hold' } | { kind: 'exit-confirmed' };

/** The step card, the evidence gates, the standing (loss, halts) and the history of one account (read only); a button only asks the tab to open a dialog. */
export function RampAccount({ accountId, onAct }: { accountId: string; onAct: (act: RampAct) => void }) {
  const q = useQuery(rampQuery(accountId));
  if (q.isPending) return <LoadingState label="Loading the step…" />;
  if (q.isError) return <ErrorState error={q.error} />;
  const r = q.data;
  const canLower = r.writesEnabled && r.step !== null && r.step > 1;
  const canRaise =
    r.writesEnabled &&
    r.step !== null &&
    r.nextStep !== null &&
    r.eligible &&
    !r.hold &&
    (r.nextStep < 3 || r.exitConfirmed);
  const raiseWhy = !r.writesEnabled
    ? 'writing is switched off'
    : r.hold
      ? 'a hold blocks raising'
      : r.nextStep === 3 && !r.exitConfirmed
        ? 'S3 needs your recorded exit confirmation first'
        : !r.eligible
          ? 'not every evidence gate passes'
          : null;
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
        <Typography variant="body2" color="text.secondary">{`${String(r.sessions)} sessions at this step.`}</Typography>
      </section>
      <RampGatesTable gates={r.gates} />
      <RampStanding ramp={r} />
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
        {r.step === null && (
          <Button
            variant="contained"
            disabled={!r.writesEnabled}
            onClick={() => {
              onAct({ kind: 'step', step: null });
            }}
          >
            Set the first step
          </Button>
        )}
        {r.step !== null && r.nextStep !== null && (
          <Button
            variant="contained"
            disabled={!canRaise}
            onClick={() => {
              onAct({ kind: 'step', step: r.step, raise: true });
            }}
          >
            {`Move to S${String(r.nextStep)}`}
          </Button>
        )}
        {r.step !== null && (
          <Button
            variant="outlined"
            disabled={!canLower}
            onClick={() => {
              onAct({ kind: 'step', step: r.step });
            }}
          >
            Lower the step
          </Button>
        )}
        {r.hold && (
          <Button
            variant="outlined"
            disabled={!r.writesEnabled}
            onClick={() => {
              onAct({ kind: 'clear-hold' });
            }}
          >
            Clear hold
          </Button>
        )}
        {r.step === 2 && !r.exitConfirmed && (
          <Button
            variant="outlined"
            disabled={!r.writesEnabled}
            onClick={() => {
              onAct({ kind: 'exit-confirmed' });
            }}
          >
            Record exit confirmation
          </Button>
        )}
      </Stack>
      {r.step !== null && r.nextStep !== null && raiseWhy && (
        <Typography variant="body2" color="text.secondary">
          {`Move to S${String(r.nextStep)} is unavailable: ${raiseWhy}.`}
        </Typography>
      )}
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
      {r.autoRampUp && (
        <Typography variant="body2" color="text.secondary">
          Raise S1 to S2 by rule is ON: when every gate passes the system raises it itself, never above S2.
        </Typography>
      )}
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
