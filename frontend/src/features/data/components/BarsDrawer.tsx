import CloseIcon from '@mui/icons-material/Close';
import { Alert, Box, Button, Drawer, IconButton, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { LoadingState } from '@/shared/ui';
import { monoFontFamily } from '@/theme';
import { barsQuery } from '../api/dataQueries';
import { problemText } from '../lib/errors';
import { isTimeframe, timeframeLabels, timeframes, type Timeframe } from '../lib/timeframes';
import { BarsTable } from './BarsTable';

interface BarsDrawerProps {
  /** The open instrument; null keeps the drawer closed. */
  id: string | null;
  timeframe: Timeframe;
  onTimeframeChange: (timeframe: Timeframe) => void;
  onClose: () => void;
}

/** Side panel with the latest bars of one instrument in a timeframe of the owner's choice. */
export function BarsDrawer({ id, timeframe, onTimeframeChange, onClose }: BarsDrawerProps) {
  const query = useQuery({ ...barsQuery(id ?? '', timeframe), enabled: id !== null });
  const { data, isPending, isError, error } = query;

  return (
    <Drawer
      anchor="right"
      open={id !== null}
      onClose={onClose}
      slotProps={{ paper: { 'aria-label': 'Bars', sx: { width: { xs: '100%', sm: 560, md: 760 }, maxWidth: '100%' } } }}
    >
      <Stack direction="row" sx={{ p: 2, gap: 1, alignItems: 'flex-start', justifyContent: 'space-between' }}>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="h2" sx={{ fontFamily: monoFontFamily, overflowWrap: 'anywhere' }}>
            {data?.symbol ?? 'Bars'}
          </Typography>
          {data && <Typography color="text.secondary">{data.name}</Typography>}
        </Box>
        <IconButton aria-label="Close bars" edge="end" onClick={onClose}>
          <CloseIcon />
        </IconButton>
      </Stack>
      <Stack spacing={2} sx={{ px: 2, pt: 1, pb: 3, overflowY: 'auto' }}>
        <TextField
          select
          size="small"
          label="Timeframe"
          value={timeframe}
          onChange={(event) => {
            if (isTimeframe(event.target.value)) onTimeframeChange(event.target.value);
          }}
          sx={{ maxWidth: 240 }}
        >
          {timeframes.map((value) => (
            <MenuItem key={value} value={value}>
              {timeframeLabels[value]}
            </MenuItem>
          ))}
        </TextField>
        {id !== null && isPending && <LoadingState label="Loading bars" />}
        {isError && (
          <Alert
            severity="error"
            action={
              <Button
                color="inherit"
                size="small"
                onClick={() => {
                  void query.refetch();
                }}
              >
                Retry
              </Button>
            }
          >
            {problemText(error, 'The bars could not be loaded.')}
          </Alert>
        )}
        {data?.items.length === 0 && (
          <Typography>
            No {timeframeLabels[timeframe]} bars yet for this instrument.
            {timeframe === '1m' ? ' Load its CSV file or fetch from Dhan.' : ' Rebuild the aggregates to create them.'}
          </Typography>
        )}
        {data && data.items.length > 0 && (
          <>
            <Typography variant="body2" color="text.secondary">
              Showing the latest {data.items.length} bars, newest first.
            </Typography>
            <BarsTable timeframe={timeframe} bars={data.items} />
          </>
        )}
      </Stack>
    </Drawer>
  );
}
