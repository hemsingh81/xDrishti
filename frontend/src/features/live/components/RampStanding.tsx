import { Alert, Stack, Typography } from '@mui/material';
import { formatDateTime } from '@/shared/lib/format';
import type { RampView } from '../api/rampQueries';
import { haltActions, measureStates, num } from '../lib/rampLabels';

/** Breach rules, what still holds a hold, the loss of today and the halts of the last 30 days (read only). A halt is advice: the owner places every order. */
export function RampStanding({ ramp }: { ramp: RampView }) {
  const loss = ramp.loss;
  return (
    <Stack sx={{ gap: 1 }}>
      {ramp.breaches.length > 0 && (
        <Alert severity="warning">
          Breach rules firing now:{' '}
          {ramp.breaches
            .map((b) => `${b.code} (${b.action === 'hold' ? 'hold' : 'step down'}): ${b.reason}`)
            .join('; ')}
          .
        </Alert>
      )}
      {ramp.hold && ramp.holdConditions.length > 0 && (
        <Typography variant="body2">
          The hold cannot be cleared yet: {ramp.holdConditions.map((b) => `${b.code} ${b.reason}`).join('; ')}.
        </Typography>
      )}
      {loss && (
        <section aria-label="Loss of the day">
          <Typography variant="h3" component="h3">
            Loss of the day
          </Typography>
          <Typography>
            {loss.state === 'unavailable'
              ? `The loss of ${loss.date} cannot be measured (capture ${loss.health.replace('_', ' ')}); that is not the same as no loss.`
              : `${loss.date}: ${num(loss.lossR, 2)} R lost of a limit of ${num(loss.limitR, 2)} R (1 R is ${num(loss.oneRupees, 2)} rupees), ${measureStates[loss.state] ?? loss.state}.`}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {loss.note} At small steps the R limit binds long before the percentage limit.
          </Typography>
        </section>
      )}
      {ramp.halts.length === 0 ? (
        <Typography>No halt in the last 30 days.</Typography>
      ) : (
        <table aria-label="Halts">
          <thead>
            <tr>
              <th scope="col">Opened</th>
              <th scope="col">Kind</th>
              <th scope="col">Period from</th>
              <th scope="col">Loss</th>
              <th scope="col">Does</th>
            </tr>
          </thead>
          <tbody>
            {ramp.halts.map((h) => (
              <tr key={h.id}>
                <td>{formatDateTime(h.openedAt)}</td>
                <td>{h.kind === 'daily' ? 'Daily' : 'Weekly'}</td>
                <td>{h.periodStart}</td>
                <td>{`${num(h.measuredR, 2)} R of ${num(h.limitR, 2)} R (${measureStates[h.measureState] ?? h.measureState})`}</td>
                <td>{haltActions[h.action] ?? h.action}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Stack>
  );
}
