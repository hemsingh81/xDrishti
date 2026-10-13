import { Alert, Box, Card, Stack, Typography } from '@mui/material';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { formatWeekdayDate, istDayIso } from '@/shared/lib/format';
import { useWallClock } from '@/shared/lib/useWallClock';
import { LoadingState } from '@/shared/ui';
import { timelineQuery } from '../api/servicesQueries';
import { problemText } from '../lib/errors';
import { timelineSummary } from '../lib/timeline';
import { QueryAlert } from './QueryAlert';
import { TimelineTrack } from './TimelineTrack';

/**
 * The trading-day timeline: the market session, the review cutoff, what every enabled service does that day and, for
 * today, where the clock is. On a date that is not a trading day it says so, because services set to trading days only
 * do not run then.
 */
export function TimelineCard() {
  // The day in India, checked once a minute: at midnight the timeline is read again (the old one stays until then).
  const today = istDayIso(useWallClock(60_000));
  const timeline = useQuery({ ...timelineQuery(undefined, today), placeholderData: keepPreviousData });
  const data = timeline.data;
  return (
    <Card component="section" aria-label="Trading-day timeline" sx={{ mb: 1.5, flexShrink: 0 }}>
      <Stack
        direction="row"
        sx={{ px: 2, pt: 1.5, columnGap: 2, rowGap: 0.25, flexWrap: 'wrap', alignItems: 'baseline' }}
      >
        <Typography variant="h2">Trading-day timeline</Typography>
        {data && (
          <Typography variant="body2" color="text.secondary">
            {formatWeekdayDate(data.date)} · {timelineSummary(data)}
          </Typography>
        )}
        {data && (
          <Typography variant="caption" color="text.secondary" sx={{ display: { sm: 'none' } }}>
            Swipe sideways to see the whole day.
          </Typography>
        )}
      </Stack>
      <Box sx={{ pt: 1, pb: 1 }}>
        {timeline.isPending && <LoadingState label="Loading the timeline" />}
        {timeline.isError && (
          <Box sx={{ px: 2 }}>
            <QueryAlert
              message={problemText(timeline.error, 'The timeline could not be loaded.')}
              onRetry={() => {
                void timeline.refetch();
              }}
            />
          </Box>
        )}
        {data && !data.tradingDay && (
          <Alert severity="info" sx={{ mx: 2, mb: 1 }}>
            {formatWeekdayDate(data.date)} is not a trading day: the market is closed, and services set to trading days
            only do not run.
          </Alert>
        )}
        {data && <TimelineTrack timeline={data} />}
      </Box>
    </Card>
  );
}
