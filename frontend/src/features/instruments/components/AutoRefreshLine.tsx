import ScheduleIcon from '@mui/icons-material/ScheduleOutlined';
import { Stack, Typography } from '@mui/material';
import type { AutoRefresh } from '@/shared/api';
import { formatIstTimeAndDay } from '@/shared/lib/format';
import { numeric } from '@/theme';

/** "Daily at 08:45 IST · next run 08:45, 12 Oct" or "Automatic refresh is off". */
export function AutoRefreshLine({ auto }: { auto: AutoRefresh }) {
  const text = auto.enabled
    ? `Daily at ${auto.timeIst} IST${auto.nextRunAt ? ` · next run ${formatIstTimeAndDay(auto.nextRunAt)} IST` : ''}`
    : 'Automatic refresh is off';
  return (
    <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center', color: 'text.secondary' }}>
      <ScheduleIcon fontSize="small" aria-hidden />
      <Typography variant="body2" sx={numeric}>
        {text}
      </Typography>
    </Stack>
  );
}
