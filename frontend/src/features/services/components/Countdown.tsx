import { Typography } from '@mui/material';
import { useWallClock } from '@/shared/lib/useWallClock';
import { numeric } from '@/theme';
import { untilText } from '../lib/countdown';

/** "in 3 h 12 min" until a moment (ISO): it moves once a minute, on the shared clock. */
export function Countdown({ to }: { to: string }) {
  const now = useWallClock(60_000);
  return (
    <Typography variant="caption" sx={{ fontWeight: 650, color: 'primary.main', ...numeric }}>
      {untilText(Date.parse(to), now)}
    </Typography>
  );
}
