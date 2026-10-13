import { Alert, Box, Card, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatWeekdayDate } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import { timelineQuery } from '../api/servicesQueries';
import { problemText } from '../lib/errors';
import { timelineSummary } from '../lib/timeline';
import { QueryAlert } from './QueryAlert';
import { TimelineTrack } from './TimelineTrack';

/**
 * The trading-day timeline: the market session, the review cutoff, and what every enabled service does that day. On a
 * date that is not a trading day it says so, because services set to trading days only do not run then.
 */
export function TimelineCard() {
  const timeline = useQuery(timelineQuery());
  const data = timeline.data;
  return (
    <Card component="section" aria-label="Trading-day timeline" sx={{ mb: 1.5, flexShrink: 0 }}>
      <Stack
        direction="row"
        sx={{ px: 2, pt: 1.25, columnGap: 2, rowGap: 0.25, flexWrap: 'wrap', alignItems: 'baseline' }}
      >
        <Typography variant="h2">Trading-day timeline</Typography>
        {data && (
          <Typography variant="body2" color="text.secondary">
            {formatWeekdayDate(data.date)} · {timelineSummary(data)}
          </Typography>
        )}
      </Stack>
      <Box sx={{ px: 2, pt: 1, pb: 1 }}>
        {timeline.isPending && <LoadingState label="Loading the timeline" />}
        {timeline.isError && (
          <QueryAlert
            message={problemText(timeline.error, 'The timeline could not be loaded.')}
            onRetry={() => {
              void timeline.refetch();
            }}
          />
        )}
        {data && !data.tradingDay && (
          <Alert severity="info" sx={{ mb: 1 }}>
            {formatWeekdayDate(data.date)} is not a trading day: the market is closed, and services set to trading days
            only do not run.
          </Alert>
        )}
        {data && <TimelineTrack timeline={data} />}
      </Box>
    </Card>
  );
}
