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
import { queueQuery } from '../api/governanceQueries';

/** Accepted hypotheses waiting. The system never applies, proposes to apply or reorders them (read only). */
export function QueueCard() {
  const q = useQuery(queueQuery);
  if (q.isPending) return <LoadingState label="Loading the queue…" />;
  if (q.isError) return <ErrorState error={q.error} />;
  return (
    <Card variant="outlined">
      <CardHeader title="Queue: accepted hypotheses waiting" />
      <CardContent>
        {q.data.items.length === 0 ? (
          <Typography>No accepted hypothesis is waiting.</Typography>
        ) : (
          <Table size="small" aria-label="Accepted hypotheses waiting">
            <TableHead>
              <TableRow>
                <TableCell>Hypothesis</TableCell>
                <TableCell>Cell</TableCell>
                <TableCell>Knob</TableCell>
                <TableCell>Change</TableCell>
                <TableCell align="right">Age (days)</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {q.data.items.map((i) => (
                <TableRow key={i.hypothesisId}>
                  <TableCell>H{i.hypothesisId}</TableCell>
                  <TableCell sx={{ wordBreak: 'break-word' }}>{i.cellKey ?? '—'}</TableCell>
                  <TableCell>{i.knob}</TableCell>
                  <TableCell>
                    {i.from} → {i.to}
                  </TableCell>
                  <TableCell align="right">{i.ageDays}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
        <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
          {q.data.note}.
        </Typography>
      </CardContent>
    </Card>
  );
}
