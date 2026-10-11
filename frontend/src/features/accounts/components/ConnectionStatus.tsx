import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlineOutlined';
import LinkOffIcon from '@mui/icons-material/LinkOffOutlined';
import ScheduleIcon from '@mui/icons-material/ScheduleOutlined';
import { Chip, Stack, Typography, type ChipProps } from '@mui/material';
import type { ReactElement } from 'react';
import type { BrokerConnection } from '@/shared/api';
import { formatHoursMinutes, formatIstTimeAndDay } from '@/shared/lib/format';
import { useConnectionState } from '../hooks/useConnectionState';
import { connectionStatusLabels, type ConnectionStatusKey } from '../lib/connection';

const looks: Record<ConnectionStatusKey, { color: ChipProps['color']; icon: ReactElement }> = {
  connected: { color: 'success', icon: <CheckCircleIcon /> },
  expired: { color: 'warning', icon: <ScheduleIcon /> },
  not_connected: { color: 'default', icon: <LinkOffIcon /> },
};

/**
 * Connection status: a chip with icon and words (never colour alone), the token's expiry in IST ("valid until 03:30,
 * 11 Oct") and, in its last two hours, a countdown ("expires in 1 h 20 min") that moves every ~30 s.
 */
export function ConnectionStatus({ connection }: { connection: BrokerConnection }) {
  const { status, expiresAt, secondsLeft } = useConnectionState(connection);
  const look = looks[status];
  let expiry: string | null = null;
  if (expiresAt && status === 'connected') {
    expiry = `valid until ${formatIstTimeAndDay(expiresAt)}`;
  } else if (expiresAt && status === 'expired') {
    expiry = `expired at ${formatIstTimeAndDay(expiresAt)}`;
  }

  return (
    <Stack direction="row" sx={{ alignItems: 'center', flexWrap: 'wrap', columnGap: 1, rowGap: 0.5 }}>
      <Chip
        size="small"
        variant="outlined"
        color={look.color}
        icon={look.icon}
        label={connectionStatusLabels[status]}
      />
      {expiry && (
        <Typography component="span" variant="body2">
          {expiry}
        </Typography>
      )}
      {secondsLeft !== null && (
        <Typography
          component="span"
          variant="body2"
          sx={{ color: 'warning.main', fontWeight: 600, fontVariantNumeric: 'tabular-nums' }}
        >
          {`expires in ${formatHoursMinutes(secondsLeft)}`}
        </Typography>
      )}
    </Stack>
  );
}
