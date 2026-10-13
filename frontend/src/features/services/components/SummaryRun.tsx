import { Box, Stack, Typography } from '@mui/material';
import { formatIstDayAndTime } from '@/shared/lib/format';
import { Countdown } from './Countdown';
import { RunStatusChip } from './RunStatusChip';

interface SummaryRunProps {
  /** "Next run" or "Last run". */
  label: string;
  /** The moment (ISO); null = nothing to show. */
  at: string | null;
  serviceName?: string;
  /** Counts down to the moment (for the next run). */
  countdown?: boolean;
  /** How the run ended (for the last run). */
  status?: string;
  empty: string;
}

/** The next run of all services, or the last run of all: when, which service, and the countdown or the outcome. */
export function SummaryRun({ label, at, serviceName, countdown = false, status, empty }: SummaryRunProps) {
  return (
    <Box sx={{ minWidth: 0 }}>
      <Typography variant="overline" color="text.secondary" component="div" sx={{ lineHeight: 1.4 }}>
        {label}
      </Typography>
      {at === null ? (
        <Typography variant="body2" color="text.secondary">
          {empty}
        </Typography>
      ) : (
        <Stack direction="row" useFlexGap sx={{ columnGap: 1, rowGap: 0.25, alignItems: 'center', flexWrap: 'wrap' }}>
          <Typography component="time" dateTime={at} variant="body2" sx={{ fontWeight: 700 }}>
            {formatIstDayAndTime(at)}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            IST
          </Typography>
          <Typography variant="body2">{serviceName}</Typography>
          {countdown && <Countdown to={at} />}
          {status && <RunStatusChip status={status} />}
        </Stack>
      )}
    </Box>
  );
}
