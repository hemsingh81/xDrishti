import { Box, Typography } from '@mui/material';
import type { RecoverySession } from '@/shared/api/types';
import { formatWeekdayDate } from '@/shared/lib/format';
import { RecoveryRow } from './RecoveryRow';

/** Sessions oldest first, each with its services. */
export function RecoverySessions({ sessions, label }: { sessions: RecoverySession[]; label: string }) {
  return (
    <Box component="section" aria-label={label}>
      {sessions.map((session) => (
        <Box key={session.session} sx={{ mb: 1.5 }}>
          <Typography variant="h3" sx={{ fontSize: '1rem' }}>
            {formatWeekdayDate(session.session)}
          </Typography>
          <Box component="ul" sx={{ listStyle: 'none', m: 0, p: 0 }}>
            {session.items.map((row) => (
              <RecoveryRow key={`${row.serviceId}-${row.from}-${row.to}`} row={row} />
            ))}
          </Box>
        </Box>
      ))}
    </Box>
  );
}
