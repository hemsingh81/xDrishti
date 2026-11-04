import { Box, Chip, Table, TableBody, TableCell, TableRow, Typography } from '@mui/material';
import type { Schemas } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { HoldOutCard } from './HoldOutCard';

const factor = (v: number): string => `x${formatNumber(v)}`;

/**
 * The pre-registration state, the count of looks with its parts and the hold-out (CF-06, AC12), from the stored report.
 * Absent in a report built before it. Every status is a word.
 */
export function ReportConfidence({ report }: { report: Schemas['LabReportView'] }) {
  const { registration, trials, holdOut } = report;
  if (!registration && !trials && !holdOut) return null;
  return (
    <Box component="section" aria-label="Registration, trials and hold-out" sx={{ mt: 1 }}>
      {registration && (
        <Box sx={{ mb: 2 }}>
          <Typography variant="subtitle2">Pre-registration</Typography>
          <Chip
            size="small"
            variant="outlined"
            color={registration.state === 'registered' ? 'success' : 'warning'}
            label={registration.state === 'registered' ? 'Registered' : 'Not registered'}
            sx={{ mr: 1 }}
          />
          <Typography component="span" variant="body2">
            {registration.text} {registration.enforce ? 'Enforced.' : 'Report only: nothing is refused.'}
          </Typography>
        </Box>
      )}
      {trials && (
        <Box sx={{ mb: 2 }}>
          <Typography variant="subtitle2">Trials counted</Typography>
          <Typography variant="body2">
            {`${trials.total} ${trials.total === 1 ? 'look' : 'looks'} at this strategy. Honest factor ${factor(trials.honestFactor)}; the gates were tightened by ${factor(trials.factorUsed)} (${trials.honestCountApplied ? 'the honest count is applied' : 'the honest count is shown but not applied yet'}).`}
          </Typography>
          <Table size="small" aria-label="Trial count parts">
            <TableBody>
              {[
                ['Distinct strategy texts', trials.runHashes],
                ['Extra exit candidates', trials.extraExits],
                ['Replans', trials.replans],
                ['Sweep points', trials.sweepPoints],
                ['Null rows', trials.nullRows],
              ].map(([label, n]) => (
                <TableRow key={label}>
                  <TableCell component="th" scope="row">
                    {label}
                  </TableCell>
                  <TableCell align="right">{n}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Typography variant="caption" component="p" color="text.secondary">
            {trials.text}
          </Typography>
        </Box>
      )}
      {holdOut && <HoldOutCard runId={report.runId} holdOut={holdOut} />}
    </Box>
  );
}
