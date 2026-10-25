import { Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { NonValue } from '@/shared/analytics';
import type { ModelDetail } from '../api/learningQueries';

const places = new Intl.NumberFormat('en-IN', { minimumFractionDigits: 4, maximumFractionDigits: 4 });

/** The map from raw score to probability. Not a reliability diagram (P8-10 will hold that). */
export function CalibrationTable({ points }: { points: ModelDetail['calibration'] }) {
  if (points.length === 0) {
    return <NonValue state={{ kind: 'empty', reason: 'No calibration table was stored.' }} />;
  }
  return (
    <>
      <Typography variant="caption" color="text.secondary" component="p" sx={{ mb: 1 }}>
        The map from score to probability, not a reliability diagram.
      </Typography>
      <TableContainer role="region" aria-label="Calibration map, scrolls sideways" tabIndex={0} sx={{ maxHeight: 320 }}>
        <Table size="small" stickyHeader aria-label="Calibration map">
          <TableHead>
            <TableRow>
              <TableCell>Raw score</TableCell>
              <TableCell align="right">Probability</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {points.map((p, index) => (
              <TableRow key={index}>
                <TableCell>{places.format(p.score)}</TableCell>
                <TableCell align="right">{places.format(p.probability)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </>
  );
}
