import { Box, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import type { RecoverySession } from '@/shared/api/types';
import { formatWeekdayDate } from '@/shared/lib/format';
import { RecoveryRow } from './RecoveryRow';

/** Sessions oldest first, each a compact table of its services (it scrolls sideways on a phone rather than squeezing). */
export function RecoverySessions({ sessions, label }: { sessions: RecoverySession[]; label: string }) {
  return (
    <Box component="section" aria-label={label}>
      {sessions.map((session) => (
        <TableContainer key={session.session} sx={{ mb: 1.5, overflowX: 'auto' }}>
          <Table size="small" aria-label={`Session ${formatWeekdayDate(session.session)}`}>
            <caption style={{ captionSide: 'top', textAlign: 'left' }}>
              <Typography component="span" sx={{ fontWeight: 600, color: 'text.primary' }}>
                {formatWeekdayDate(session.session)}
              </Typography>
            </caption>
            <TableHead>
              <TableRow>
                <TableCell>Service</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>What happened</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {session.items.map((row) => (
                <RecoveryRow key={`${row.serviceId}-${row.from}-${row.to}`} row={row} />
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      ))}
    </Box>
  );
}
