import { Box, Skeleton, Stack } from '@mui/material';
import { visuallyHidden } from '@mui/utils';

/** Placeholder rows for a list that is on its way (never the rows of another list). */
export function ActionListSkeleton() {
  return (
    <Box role="status" aria-live="polite" aria-busy>
      <Box component="span" sx={visuallyHidden}>
        Loading the Action center
      </Box>
      <Stack spacing={1.5} aria-hidden>
        <Skeleton variant="rounded" height={28} width={220} />
        <Skeleton variant="rounded" height={56} />
        <Skeleton variant="rounded" height={56} />
      </Stack>
    </Box>
  );
}
