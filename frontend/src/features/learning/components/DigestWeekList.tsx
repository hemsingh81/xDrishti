import { Box, ListItemButton, Typography } from '@mui/material';
import { formatAsOf } from '@/shared/analytics';
import { formatDate } from '@/shared/lib/format';
import type { Digest } from '../api/learningQueries';

/** The stored weeks, newest first; the selected one is marked in words and with aria-current. */
export function DigestWeekList({
  digests,
  selected,
  onSelect,
}: {
  digests: readonly Digest[];
  selected: string;
  onSelect: (week: string) => void;
}) {
  return (
    <Box
      component="ul"
      aria-label="Stored digests, newest week first"
      sx={{ m: 0, p: 0, listStyle: 'none', display: 'grid' }}
    >
      {digests.map((d) => (
        <li key={d.isoWeek}>
          <ListItemButton
            selected={d.isoWeek === selected}
            aria-current={d.isoWeek === selected ? 'true' : undefined}
            onClick={() => {
              onSelect(d.isoWeek);
            }}
            sx={{ minHeight: 44, display: 'grid', justifyItems: 'start' }}
          >
            <Typography sx={{ fontWeight: 700 }}>
              {d.isoWeek}
              {d.isoWeek === selected ? ' (shown)' : ''}
            </Typography>
            <Typography variant="caption" color="text.secondary">
              {formatDate(d.weekStart)} to {formatDate(d.weekEnd)}; built by {d.builtBy ?? 'unknown'}; stored{' '}
              {formatAsOf(d.createdAt)}
            </Typography>
          </ListItemButton>
        </li>
      ))}
    </Box>
  );
}
