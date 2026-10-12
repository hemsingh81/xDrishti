import CloseIcon from '@mui/icons-material/Close';
import { Alert, Box, Button, Chip, Drawer, IconButton, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import type { BasketHistoryEntry } from '@/shared/api';
import { formatDateTime, formatNumber } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import { monoFontFamily } from '@/theme';
import { historyLimit, historyQuery } from '../api/basketsQueries';
import { basketProblemText } from '../lib/errors';

interface HistoryDrawerProps {
  basketId: string;
  basketName: string;
  open: boolean;
  onClose: () => void;
}

function Entry({ entry }: { entry: BasketHistoryEntry }) {
  const added = entry.kind === 'added';
  return (
    <Box component="li" sx={{ listStyle: 'none', py: 1.25, borderBottom: 1, borderColor: 'divider' }}>
      <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
        {/* The word, not just a colour. */}
        <Chip
          size="small"
          variant="outlined"
          color={added ? 'success' : 'default'}
          label={added ? 'Added' : 'Removed'}
        />
        <Typography sx={{ fontFamily: monoFontFamily, fontWeight: 600 }}>{entry.symbol}</Typography>
        <Typography color="text.secondary" sx={{ minWidth: 0 }}>
          {entry.name}
        </Typography>
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
        {[formatDateTime(entry.at), entry.by ? `by ${entry.by}` : null].filter(Boolean).join(' · ')}
      </Typography>
      {entry.note && <Typography variant="body2">{`Note: ${entry.note}`}</Typography>}
    </Box>
  );
}

/** The newest additions and removals of the basket (at most `historyLimit`), newest first, with date, note and who. */
export function HistoryDrawer({ basketId, basketName, open, onClose }: HistoryDrawerProps) {
  const query = useQuery({ ...historyQuery(basketId), enabled: open });
  const { data, isPending, isError, error } = query;
  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      slotProps={{ paper: { 'aria-label': `History of ${basketName}`, sx: { width: { xs: '100%', sm: 460 } } } }}
    >
      <Stack direction="row" sx={{ p: 2, gap: 1, alignItems: 'flex-start', justifyContent: 'space-between' }}>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="h2">History</Typography>
          <Typography color="text.secondary">{basketName}</Typography>
          <Typography variant="body2" color="text.secondary">
            {`The newest ${formatNumber(historyLimit)} additions and removals`}
          </Typography>
        </Box>
        <IconButton aria-label="Close history" edge="end" onClick={onClose}>
          <CloseIcon />
        </IconButton>
      </Stack>
      <Box sx={{ px: 2, pb: 3, overflowY: 'auto' }}>
        {isPending && <LoadingState label="Loading history" />}
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
            {basketProblemText(error, 'The history could not be loaded.')}
          </Alert>
        )}
        {data?.items.length === 0 && (
          <Typography color="text.secondary">Nothing has been added or removed yet.</Typography>
        )}
        {data && data.items.length >= historyLimit && (
          <Typography variant="body2" color="text.secondary" sx={{ pb: 1 }}>
            {`Showing the newest ${formatNumber(historyLimit)} changes. Older ones are not shown.`}
          </Typography>
        )}
        {data && data.items.length > 0 && (
          <Box component="ul" aria-label="Changes" sx={{ m: 0, p: 0 }}>
            {data.items.map((entry) => (
              <Entry key={`${entry.memberId}-${entry.kind}`} entry={entry} />
            ))}
          </Box>
        )}
      </Box>
    </Drawer>
  );
}
