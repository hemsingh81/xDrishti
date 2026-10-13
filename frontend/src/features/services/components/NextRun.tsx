import { Box, Typography } from '@mui/material';
import { visuallyHidden } from '@mui/utils';
import type { ServiceView } from '@/shared/api';
import { nextRunText } from '../lib/nextRun';

/** The next scheduled start in India time; "After …" for a run-after service; a dash (read as "not scheduled") when none. */
export function NextRun({ service, names }: { service: ServiceView; names: ReadonlyMap<string, string> }) {
  const text = nextRunText(service, names);
  if (text === null) {
    return (
      <Typography variant="body2" color="text.secondary">
        <span aria-hidden>—</span>
        <Box component="span" sx={visuallyHidden}>
          Not scheduled
        </Box>
      </Typography>
    );
  }
  return service.nextRun && service.enabled ? (
    <Typography component="time" variant="body2" dateTime={service.nextRun}>
      {text}
    </Typography>
  ) : (
    <Typography variant="body2">{text}</Typography>
  );
}
