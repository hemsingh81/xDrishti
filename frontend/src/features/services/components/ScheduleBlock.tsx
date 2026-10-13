import { Typography } from '@mui/material';
import type { ServiceView } from '@/shared/api';
import { CardFact } from './CardFact';
import { ExecutionChips } from './ExecutionChips';
import { ServiceIssues } from './ServiceIssues';

/**
 * The schedule in words, then retries, timeout and catch-up as chips, then what the checks found about it. An entry that
 * is set elsewhere (the per-account sync) has no retries or timeout of its own, so no chips.
 */
export function ScheduleBlock({ service }: { service: ServiceView }) {
  return (
    <CardFact label="Schedule">
      <Typography variant="body2" sx={{ fontWeight: 500 }}>
        {service.scheduleWords}
      </Typography>
      {!service.readOnly && <ExecutionChips execution={service.execution} />}
      <ServiceIssues issues={service.issues} />
    </CardFact>
  );
}
