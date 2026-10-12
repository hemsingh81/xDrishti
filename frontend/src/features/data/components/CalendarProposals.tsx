import { Box, Typography } from '@mui/material';
import { alpha } from '@mui/material/styles';
import { useId } from 'react';
import type { CalendarDay } from '@/shared/api';
import { CalendarList } from './CalendarList';

/** Days found in the stored bars that wait for the owner: never a holiday or special session until confirmed. */
export function CalendarProposals({ days }: { days: readonly CalendarDay[] }) {
  const titleId = useId();
  return (
    <Box
      component="section"
      aria-labelledby={titleId}
      sx={(theme) => ({
        border: 1,
        borderColor: 'warning.main',
        borderRadius: 1,
        bgcolor: alpha(theme.palette.warning.main, 0.06),
        p: 1.5,
      })}
    >
      <Typography id={titleId} variant="h3" component="h2" sx={{ mb: 0.5 }}>
        Proposed ({days.length})
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        Found in your stored data — confirm to treat it as a non-trading day / special session, or dismiss it and it
        will not be proposed again.
      </Typography>
      <CalendarList label="Proposed calendar days" days={days} proposed />
    </Box>
  );
}
