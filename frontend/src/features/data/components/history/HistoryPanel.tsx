import { Alert, Box, Button, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { historyQuery } from '../../api/historyQueries';
import { HistoryActs } from './HistoryActs';
import { HistoryTable } from './HistoryTable';
import { HistoryTotals } from './HistoryTotals';

/** Data → History: how deep the history of each tracked instrument is and where the 1-minute backfill stands. The switch is read-only. */
export default function HistoryPanel() {
  const query = useQuery(historyQuery);
  const data = query.data;
  return (
    <Box
      sx={{
        flex: '1 1 auto',
        minHeight: 0,
        minWidth: 0,
        overflowY: 'auto',
        display: 'grid',
        gap: 2,
        alignContent: 'start',
      }}
    >
      {query.isPending && <LoadingState label="Loading history" />}
      {query.isError && (
        <Box>
          <ErrorState error={query.error} />
          <Button
            onClick={() => {
              void query.refetch();
            }}
          >
            Try again
          </Button>
        </Box>
      )}
      {data && (
        <>
          {!data.switch.enabled && (
            <Alert severity="info">
              The 1-minute backfill is off. It stays off until source-aware replay (CF-04d) is built.
            </Alert>
          )}
          <HistoryTotals data={data} />
          <HistoryActs data={data} />
          {data.instruments.length === 0 ? (
            <Typography variant="body2">No tracked instrument has CSV bars yet.</Typography>
          ) : (
            <HistoryTable rows={data.instruments} />
          )}
          <Typography variant="caption" color="text.secondary" component="p">
            Limits: the switch is a deployment setting and cannot be turned on from here. Backfilled bars are not used
            by any run until the seam is checked. Daily history comes from the bhavcopy files and is shown as one span.
            A probe asks Dhan for a few instruments only. The list is not paged.
          </Typography>
        </>
      )}
    </Box>
  );
}
