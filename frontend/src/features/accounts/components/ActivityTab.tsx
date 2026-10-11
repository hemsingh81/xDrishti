import { Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatDateTime } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { accountActivityQuery } from '../api/accountsQueries';
import { humanizeEvent } from '../lib/labels';

/** Who changed what and when (newest first). */
export function ActivityTab({ accountId }: { accountId: string }) {
  const { data, error, isPending } = useQuery(accountActivityQuery(accountId));
  if (isPending) {
    return <LoadingState label="Loading activity" />;
  }
  if (error) {
    return <ErrorState error={error} />;
  }
  if (data.length === 0) {
    return <Typography color="text.secondary">No activity yet.</Typography>;
  }
  return (
    <TableContainer>
      <Table size="small" aria-label="Account activity">
        <TableHead>
          <TableRow>
            <TableCell>When</TableCell>
            <TableCell>Actor</TableCell>
            <TableCell>Action</TableCell>
            <TableCell>Detail</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {data.map((entry) => (
            <TableRow key={`${entry.at}-${entry.event}-${entry.detail ?? ''}`}>
              <TableCell sx={{ whiteSpace: 'nowrap' }}>{formatDateTime(entry.at)}</TableCell>
              <TableCell>{entry.actor}</TableCell>
              <TableCell>{humanizeEvent(entry.event)}</TableCell>
              <TableCell sx={{ overflowWrap: 'anywhere' }}>{entry.detail ?? '—'}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
