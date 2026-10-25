import {
  Card,
  CardContent,
  CardHeader,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { epochsQuery } from '../api/governanceQueries';

/** The measurement epochs with their own counts; they are never pooled (read only). */
export function EpochsCard() {
  const q = useQuery(epochsQuery);
  if (q.isPending) return <LoadingState label="Loading the epochs…" />;
  if (q.isError) return <ErrorState error={q.error} />;
  const items = q.data.items;
  return (
    <Card variant="outlined">
      <CardHeader
        title="Epochs"
        subheader="Each epoch is counted on its own; trades around a change are set aside, shown and never counted."
      />
      <CardContent>
        {items.length === 0 ? (
          <Typography>No capture has run yet; the first epoch starts with the first capture.</Typography>
        ) : (
          <Table size="small" aria-label="Epochs">
            <TableHead>
              <TableRow>
                <TableCell>Epoch</TableCell>
                <TableCell>Kind</TableCell>
                <TableCell>From</TableCell>
                <TableCell>To</TableCell>
                <TableCell>Fingerprint</TableCell>
                <TableCell align="right">Counted</TableCell>
                <TableCell align="right">Set aside</TableCell>
                <TableCell align="right">Risk-down</TableCell>
                <TableCell align="right">Unexplained</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {items.map((e) => (
                <TableRow key={e.id}>
                  <TableCell>{e.id}</TableCell>
                  <TableCell>{e.kind}</TableCell>
                  <TableCell>{e.startedOn}</TableCell>
                  <TableCell>{e.endedOn ?? 'in force'}</TableCell>
                  <TableCell>{e.fingerprint ?? '—'}</TableCell>
                  <TableCell align="right">{e.counted}</TableCell>
                  <TableCell align="right">{e.straddle}</TableCell>
                  <TableCell align="right">{e.riskDownChanges}</TableCell>
                  <TableCell align="right">{e.unexplainedChanges}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}
