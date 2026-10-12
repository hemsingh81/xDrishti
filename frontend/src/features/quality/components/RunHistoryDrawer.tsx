import CloseIcon from '@mui/icons-material/Close';
import { Box, Drawer, IconButton, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { LoadingState } from '@/shared/ui';
import { runsQuery, runsTake } from '../api/qualityQueries';
import { problemText } from '../lib/errors';
import { QueryAlert } from './QueryAlert';
import { RunItem } from './RunItem';

/** The last runs of the checks, newest first, in a drawer opened from the header: when, who started it, the result. */
export function RunHistoryDrawer({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      slotProps={{ paper: { 'aria-label': 'Run history', sx: { width: { xs: '100%', sm: 480 }, maxWidth: '100%' } } }}
    >
      {open && <RunHistory onClose={onClose} />}
    </Drawer>
  );
}

/** Mounted only while the drawer is open, so the runs are read when the owner asks for them. */
function RunHistory({ onClose }: { onClose: () => void }) {
  const runs = useQuery(runsQuery);
  return (
    <>
      <Stack direction="row" sx={{ p: 2, gap: 1, alignItems: 'flex-start', justifyContent: 'space-between' }}>
        <Box>
          <Typography variant="h2">Run history</Typography>
          <Typography variant="body2" color="text.secondary">
            The last {runsTake} runs of the checks, newest first.
          </Typography>
        </Box>
        <IconButton aria-label="Close run history" edge="end" onClick={onClose}>
          <CloseIcon />
        </IconButton>
      </Stack>
      <Box sx={{ px: 2, pb: 3, overflowY: 'auto' }}>
        {runs.isPending && <LoadingState label="Loading recent runs" />}
        {runs.isError && (
          <QueryAlert
            message={problemText(runs.error, 'The recent runs could not be loaded.')}
            onRetry={() => {
              void runs.refetch();
            }}
          />
        )}
        {runs.data?.length === 0 && <Typography color="text.secondary">No checks have run yet.</Typography>}
        {runs.data && runs.data.length > 0 && (
          <Box component="ul" aria-label="Recent runs" sx={{ m: 0, p: 0 }}>
            {runs.data.map((run) => (
              <RunItem key={run.id} run={run} />
            ))}
          </Box>
        )}
      </Box>
    </>
  );
}
