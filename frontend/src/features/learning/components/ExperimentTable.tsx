import {
  Box,
  Button,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { formatAsOf, formatCount, formatR } from '@/shared/analytics';
import { formatDate } from '@/shared/lib/format';
import type { Experiment } from '../api/learningQueries';
import { OutcomeBadge } from './OutcomeBadge';
import { RValue } from './RValue';

const headers = [
  'Knob',
  'Change',
  'Outcome',
  'Folds',
  'Required improvement',
  'Pooled mean R',
  'Total R after the live gap',
  'Hold-out',
  'Timing',
  'Recorded',
];

const seconds = (ms: number) => (ms / 1000).toFixed(1);

/** The experiments, newest first. The server's reasons are printed verbatim under the row they belong to. */
export function ExperimentTable({ rows, onOpen }: { rows: readonly Experiment[]; onOpen: (id: number) => void }) {
  return (
    <TableContainer role="region" aria-label="Experiments, scrolls sideways" tabIndex={0}>
      <Table size="small" aria-label="Experiments">
        <TableHead>
          <TableRow>
            {headers.map((h) => (
              <TableCell key={h}>{h}</TableCell>
            ))}
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.map((e) => (
            <ExperimentRows
              key={e.id}
              e={e}
              onOpen={() => {
                onOpen(e.id);
              }}
            />
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}

function ExperimentRows({ e, onOpen }: { e: Experiment; onOpen: () => void }) {
  return (
    <>
      <TableRow sx={e.reasons.length > 0 ? { '& td, & th': { borderBottom: 0 } } : undefined}>
        <TableCell component="th" scope="row" sx={{ minWidth: 160 }}>
          <Button size="small" onClick={onOpen} sx={{ minHeight: 44, textAlign: 'left' }}>
            Experiment #{e.id}
          </Button>
          <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>
            {e.knobName} ({e.knobKind})
          </Typography>
        </TableCell>
        <TableCell sx={{ overflowWrap: 'anywhere' }}>
          {e.baselineValue} to {e.candidateValue}
        </TableCell>
        <TableCell sx={{ minWidth: 200 }}>
          <OutcomeBadge name={e.outcome} />
        </TableCell>
        <TableCell>
          {formatCount(e.foldsBetter)} of {formatCount(e.usedFolds)} used folds better; {formatCount(e.foldsNeeded)}{' '}
          needed
        </TableCell>
        <TableCell>
          {formatR(e.requiredImprovementR)} after {formatCount(e.trials)} {e.trials === 1 ? 'trial' : 'trials'} in 30
          days
        </TableCell>
        <TableCell>
          <Box>
            Baseline <RValue value={e.pooledBaselineMeanR} n={e.baselineTrades} missing="No pooled mean was stored." />
          </Box>
          <Box>
            Candidate{' '}
            <RValue value={e.pooledCandidateMeanR} n={e.candidateTrades} missing="No pooled mean was stored." />
          </Box>
        </TableCell>
        <TableCell>
          <Box>Baseline {formatR(e.baselineTotalR)}</Box>
          <Box>Candidate {formatR(e.candidateTotalR)}</Box>
        </TableCell>
        <TableCell>{e.holdOutStart === null ? 'no hold-out kept' : `from ${formatDate(e.holdOutStart)}`}</TableCell>
        <TableCell>comparison read in {seconds(e.compareMs)} s; the replay is not timed</TableCell>
        <TableCell>
          {formatAsOf(e.createdAt)} by {e.createdBy}
        </TableCell>
      </TableRow>
      {e.reasons.length > 0 && (
        <TableRow>
          <TableCell colSpan={headers.length}>
            <Box component="ul" aria-label={`Reasons for experiment ${e.id}`} sx={{ m: 0, pl: 2.5 }}>
              {e.reasons.map((reason, index) => (
                <li key={index}>{reason}</li>
              ))}
            </Box>
          </TableCell>
        </TableRow>
      )}
    </>
  );
}
