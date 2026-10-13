import { Box, Typography } from '@mui/material';
import { visuallyHidden } from '@mui/utils';
import type { ServiceView } from '@/shared/api';
import { nextRunText } from '../lib/nextRun';

/** The next scheduled start in India time, large as on a card; "After …" for a run-after service; a dash (read as "not scheduled") when none. */
export function NextRun({ service, names }: { service: ServiceView; names: ReadonlyMap<string, string> }) {
  const text = nextRunText(service, names);
  const look = { variant: 'subtitle1', sx: { fontWeight: 700 } } as const;
  if (text === null) {
    return (
      <Typography {...look} color="text.secondary">
        <span aria-hidden>—</span>
        <Box component="span" sx={visuallyHidden}>
          Not scheduled
        </Box>
      </Typography>
    );
  }
  return service.nextRun && service.enabled ? (
    <Typography component="time" {...look} dateTime={service.nextRun}>
      {text}
    </Typography>
  ) : (
    <Typography {...look}>{text}</Typography>
  );
}
