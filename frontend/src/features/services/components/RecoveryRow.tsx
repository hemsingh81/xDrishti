import { Box, Stack, Typography } from '@mui/material';
import type { RecoveryRow as Row } from '@/shared/api/types';
import { formatDate } from '@/shared/lib/format';
import { recoveryStatus } from '../lib/recoveryStatus';

/** One service at one session: status as icon and words, the service, the reason and (partial) the missing dates. */
export function RecoveryRow({ row }: { row: Row }) {
  const status = recoveryStatus(row.status);
  const Icon = status.icon;
  return (
    <Box component="li" sx={{ display: 'flex', gap: 1.25, py: 1, alignItems: 'flex-start' }}>
      <Icon fontSize="small" aria-hidden sx={{ color: status.color, mt: '2px' }} />
      <Stack sx={{ minWidth: 0, gap: 0.25 }}>
        <Typography sx={{ fontWeight: 600 }}>
          {row.serviceName}
          <Typography component="span" color="text.secondary" sx={{ fontWeight: 400 }}>
            {' — '}
            {status.label}
          </Typography>
        </Typography>
        <Typography variant="body2" color="text.secondary">
          {row.reason ?? status.fallback}
        </Typography>
        {row.status === 'partial' && row.missingSessions.length > 0 && (
          <Typography variant="body2">Still missing: {row.missingSessions.map(formatDate).join(', ')}</Typography>
        )}
        {row.attempts > 0 && (
          <Typography variant="caption" color="text.secondary">
            {row.attempts === 1 ? '1 attempt' : `${String(row.attempts)} attempts`}
          </Typography>
        )}
      </Stack>
    </Box>
  );
}
