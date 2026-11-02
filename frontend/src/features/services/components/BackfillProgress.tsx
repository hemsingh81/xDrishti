import { Box, Typography } from '@mui/material';
import type { Schemas } from '@/shared/api';
import { formatIstDayAndTime } from '@/shared/lib/format';

const count = new Intl.NumberFormat('en-IN');

/** The expired-options backfill: whether the probe result was accepted and how far the key plan is. Counts only, never a bar. */
export function BackfillProgress({ view }: { view: Schemas['OptionBackfillView'] }) {
  const { progress, probeAccepted } = view;
  return (
    <Box sx={{ mb: 1 }}>
      <Typography variant="body2">
        {probeAccepted
          ? `Probe result accepted by ${probeAccepted.actor}, ${formatIstDayAndTime(probeAccepted.acceptedAt)} IST.`
          : 'The probe result has not been accepted yet.'}
      </Typography>
      <Typography variant="body2" color="text.secondary">
        Accepting the probe result is done through the API or the command line (the xd-probe-backfill.sh flow in ADR
        0177), not on this screen.
      </Typography>
      {progress === null ? (
        <Typography variant="body2" color="text.secondary">
          No progress reported yet.
        </Typography>
      ) : (
        <>
          <Typography variant="body2" sx={{ fontWeight: 600, mt: 1 }}>
            Progress
          </Typography>
          <Typography variant="body2" component="ul" aria-label="Backfill progress" sx={{ m: 0, pl: 2.5 }}>
            <li>{`Keys planned: ${count.format(progress.planned)}`}</li>
            <li>{`Keys done: ${count.format(progress.done)}`}</li>
            <li>{`Keys empty: ${count.format(progress.empty)}`}</li>
            <li>{`Keys with no data: ${count.format(progress.noData)}`}</li>
            <li>{`Keys given up: ${count.format(progress.givenUp)}`}</li>
            <li>{`Keys left: ${count.format(progress.left)}`}</li>
            <li>
              {progress.estimatedHours === null
                ? 'Estimated finish: not known yet'
                : `Estimated finish: about ${progress.estimatedHours.toFixed(1)} hours`}
            </li>
          </Typography>
        </>
      )}
    </Box>
  );
}
