import AddIcon from '@mui/icons-material/Add';
import { Box, Button, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useId, useState } from 'react';
import { LoadingState } from '@/shared/ui';
import { calendarQuery } from '../api/dataQueries';
import { problemText } from '../lib/errors';
import { AddCalendarDayDialog } from './AddCalendarDayDialog';
import { CalendarList } from './CalendarList';
import { CalendarProposals } from './CalendarProposals';
import { InfoPopover } from './InfoPopover';
import { QueryAlert } from './QueryAlert';
import { RefreshCalendarButton } from './RefreshCalendarButton';

const none = (
  <Typography color="text.secondary" sx={{ p: 2 }}>
    No upcoming holidays or special sessions yet. Refresh from NSE, or add one by hand.
  </Typography>
);

/**
 * The trading calendar: the days found in the stored data that wait to be confirmed or dismissed come first, then the
 * confirmed holidays and special sessions ahead (Remove / Dismiss), with "Add holiday / special session" and "Refresh
 * from NSE". "Behind" and the gaps are counted in the sessions this calendar says exist. The tab scrolls as a whole.
 */
export function CalendarPanel() {
  const calendar = useQuery(calendarQuery);
  const upcomingId = useId();
  const [adding, setAdding] = useState(false);
  const data = calendar.data;

  return (
    <Box sx={{ flex: '1 1 auto', minHeight: 0, overflowY: 'auto', '& > * + *': { mt: 1.5 } }}>
      <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, alignItems: 'center' }}>
        <Button
          size="small"
          variant="outlined"
          startIcon={<AddIcon />}
          onClick={() => {
            setAdding(true);
          }}
        >
          Add holiday / special session
        </Button>
        <RefreshCalendarButton />
        <InfoPopover label="About the trading calendar">
          <Typography variant="body2">
            Holidays and special sessions decide which days count as a session, so “behind” is counted in trading days.
          </Typography>
        </InfoPopover>
      </Box>
      {calendar.isPending && <LoadingState label="Loading the trading calendar" />}
      {calendar.isError && !data && (
        <QueryAlert
          message={problemText(calendar.error, 'The trading calendar could not be loaded.')}
          onRetry={() => {
            void calendar.refetch();
          }}
        />
      )}
      {data && data.proposals.length > 0 && <CalendarProposals days={data.proposals} />}
      {data && (
        <Box component="section" aria-labelledby={upcomingId}>
          <Typography id={upcomingId} variant="h3" component="h2" sx={{ mb: 0.5 }}>
            Upcoming
          </Typography>
          <CalendarList label="Upcoming holidays and special sessions" days={data.days} empty={none} />
        </Box>
      )}
      {data && (
        <Typography variant="caption" color="text.secondary" component="p">
          Dismissed entries are not shown; to bring one back, add it again.
        </Typography>
      )}
      {adding && (
        <AddCalendarDayDialog
          onClose={() => {
            setAdding(false);
          }}
        />
      )}
    </Box>
  );
}
