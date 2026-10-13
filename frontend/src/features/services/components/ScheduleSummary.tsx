import { Box, Typography } from '@mui/material';
import type { ServiceView } from '@/shared/api';
import { executionText } from '../lib/labels';
import { ServiceIssues } from './ServiceIssues';

/** The schedule in words, then retries, timeout and catch-up, then what the checks found about it. */
export function ScheduleSummary({ service }: { service: ServiceView }) {
  return (
    <Box sx={{ whiteSpace: 'normal', minWidth: 0, maxWidth: 360 }}>
      <Typography variant="body2">{service.scheduleWords}</Typography>
      <Typography variant="caption" color="text.secondary" component="div">
        {executionText(service.execution)}
      </Typography>
      <ServiceIssues issues={service.issues} />
    </Box>
  );
}
