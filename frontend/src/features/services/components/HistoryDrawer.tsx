import CloseIcon from '@mui/icons-material/Close';
import { Box, Drawer, IconButton, Stack, Typography } from '@mui/material';
import type { ServiceRun, ServiceView } from '@/shared/api';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { Grid } from '@/shared/ui/grid';
import { runsList } from '../api/servicesQueries';
import { problemText } from '../lib/errors';
import { historyColumns } from './historyColumns';

const getRowId = (run: ServiceRun) => run.id;

interface HistoryDrawerProps {
  /** The service whose runs are shown; null = closed. */
  service: ServiceView | null;
  onClose: () => void;
}

/** The runs and attempts of a service, newest first, in a drawer: trigger, status, start, duration and message. */
export function HistoryDrawer({ service, onClose }: HistoryDrawerProps) {
  return (
    <Drawer
      anchor="right"
      open={service !== null}
      onClose={onClose}
      slotProps={{
        paper: { 'aria-label': 'Run history', sx: { width: { xs: '100%', md: 760 }, maxWidth: '100%' } },
      }}
    >
      {service && <History service={service} onClose={onClose} />}
    </Drawer>
  );
}

function NoRuns() {
  return (
    <Box sx={{ p: 4, textAlign: 'center' }}>
      <Typography sx={{ fontWeight: 600 }}>No runs yet</Typography>
      <Typography variant="body2" color="text.secondary">
        Press Run now, or wait for the next scheduled start.
      </Typography>
    </Box>
  );
}

/** Mounted only while the drawer is open, so the runs are read when the owner asks for them. */
function History({ service, onClose }: { service: ServiceView; onClose: () => void }) {
  const runs = useInfiniteRows(runsList(service.id));
  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%', minHeight: 0 }}>
      <Stack direction="row" sx={{ p: 2, gap: 1, alignItems: 'flex-start', justifyContent: 'space-between' }}>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="h2">{service.name} — run history</Typography>
          <Typography variant="body2" color="text.secondary">
            Every run and retry, newest first. Times are India time.
          </Typography>
        </Box>
        <IconButton aria-label="Close run history" edge="end" onClick={onClose}>
          <CloseIcon />
        </IconButton>
      </Stack>
      <Grid
        label="Runs"
        columns={historyColumns}
        rows={runs.rows}
        getRowId={getRowId}
        loading={runs.isPending}
        error={runs.isError ? problemText(runs.error, 'The runs could not be loaded.') : null}
        onRetry={runs.refetch}
        resetKey={service.id}
        infinite={{
          total: runs.total,
          hasMore: runs.hasMore,
          loadingMore: runs.isFetchingNextPage,
          onLoadMore: runs.loadMore,
          error: runs.loadMoreError ? problemText(runs.loadMoreError, 'The next runs could not be loaded.') : null,
          onRetryMore: runs.retryMore,
        }}
        emptyState={<NoRuns />}
      />
    </Box>
  );
}
