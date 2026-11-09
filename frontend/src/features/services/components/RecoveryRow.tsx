import { Stack, TableCell, TableRow, Typography } from '@mui/material';
import type { RecoveryRow as Row } from '@/shared/api/types';
import { formatDate } from '@/shared/lib/format';
import { recoveryStatus } from '../lib/recoveryStatus';

/** What a retry does for this row, in words (null: nothing is said). */
function retryNote(row: Row): string | null {
  if (row.retryKind === 'recheck') return 'Retry re-checks the data already stored; it fetches nothing.';
  if (row.retryKind === 'run') return 'Retry runs it again.';
  if (row.status === 'still_failing' && row.attempts > 0) {
    return `Tried ${row.attempts === 1 ? 'once' : `${String(row.attempts)} times`}; it will not be run again.`;
  }
  return null;
}

/** One service at one session: the service, its status as icon and words, and the reason (partial: the missing dates). */
export function RecoveryRow({ row }: { row: Row }) {
  const status = recoveryStatus(row.status);
  const Icon = status.icon;
  const note = retryNote(row);
  return (
    <TableRow>
      <TableCell component="th" scope="row" sx={{ fontWeight: 600 }}>
        {row.serviceName}
      </TableCell>
      <TableCell sx={{ whiteSpace: 'nowrap' }}>
        <Stack direction="row" sx={{ gap: 0.75, alignItems: 'center' }}>
          <Icon fontSize="small" aria-hidden sx={{ color: status.color }} />
          {status.label}
        </Stack>
      </TableCell>
      <TableCell>
        <Typography variant="body2">{row.reason ?? status.fallback}</Typography>
        {row.status === 'partial' && row.missingSessions.length > 0 && (
          <Typography variant="body2">Still missing: {row.missingSessions.map(formatDate).join(', ')}</Typography>
        )}
        {note && (
          <Typography variant="caption" color="text.secondary">
            {note}
          </Typography>
        )}
      </TableCell>
    </TableRow>
  );
}
