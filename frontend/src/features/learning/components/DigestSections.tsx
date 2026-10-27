import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { BriefingCard } from '@/features/briefing';
import { ApiError } from '@/shared/api';
import { NonValue } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { digestQuery } from '../api/learningQueries';
import { DrawerSection } from './DrawerSection';

const isoWeek = /^\d{4}-W\d{2}$/;

/** One week's digest: its sections as text. 404 is an ordinary answer, not a failure. */
export function DigestSections({ week }: { week: string }) {
  const valid = isoWeek.test(week);
  // A hand-edited week is the same answer as a week with no digest; nothing is asked of the server.
  const query = useQuery({ ...digestQuery(week), enabled: valid });
  if (!valid) return <NonValue state={{ kind: 'empty', reason: 'No digest stored for this week' }} />;
  if (query.isPending) return <LoadingState label="Loading the digest…" />;
  if (query.error instanceof ApiError && query.error.status === 404) {
    return <NonValue state={{ kind: 'empty', reason: 'No digest stored for this week' }} />;
  }
  if (query.isError) return <ErrorState error={query.error} />;
  return (
    <Box sx={{ display: 'grid', gap: 2 }}>
      <BriefingCard kind="weekly" subject={week} />
      {query.data.sections.map((section) => (
        <DrawerSection key={section.key} title={section.title}>
          <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
            {section.lines.map((line, index) => (
              <Typography key={index} component="li" variant="body2" sx={{ overflowWrap: 'anywhere' }}>
                {line}
              </Typography>
            ))}
          </Box>
        </DrawerSection>
      ))}
      <Typography variant="caption" color="text.secondary">
        Every section says nothing was applied.
      </Typography>
    </Box>
  );
}
