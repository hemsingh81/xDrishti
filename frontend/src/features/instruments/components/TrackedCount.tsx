import { Typography } from '@mui/material';
import type { InstrumentFacets } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';

/** "312 tracked" (and how many of them are no longer in Dhan's list) for the page header. Nothing while unknown. */
export function TrackedCount({ facets }: { facets: InstrumentFacets | undefined }) {
  if (!facets) return null;
  const retired = facets.trackedRetired > 0 ? ` (${formatNumber(facets.trackedRetired)} no longer in Dhan's list)` : '';
  return (
    <Typography variant="body2" color="text.secondary" sx={numeric}>
      {`${formatNumber(facets.tracked)} tracked${retired}`}
    </Typography>
  );
}
