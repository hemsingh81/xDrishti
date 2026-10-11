import CloseIcon from '@mui/icons-material/Close';
import { Alert, Box, Button, Chip, Drawer, IconButton, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import type { InstrumentDetail } from '@/shared/api';
import { Facts, LoadingState } from '@/shared/ui';
import { monoFontFamily } from '@/theme';
import { detailQuery } from '../api/instrumentsQueries';
import { problemText } from '../lib/errors';
import { detailFacts } from '../lib/detailFacts';
import { typeLabel } from '../lib/labels';

function Flags({ detail }: { detail: InstrumentDetail }) {
  return (
    <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 0.75 }}>
      <Chip size="small" variant="outlined" label={typeLabel(detail.type)} />
      <Chip size="small" variant="outlined" label={detail.sector ?? 'No sector'} />
      {detail.fnoEligible && <Chip size="small" variant="outlined" color="info" label="F&O" />}
      {detail.asmGsm && <Chip size="small" variant="outlined" color="warning" label="ASM/GSM" />}
      {detail.retiredAt && <Chip size="small" label="Retired" />}
    </Stack>
  );
}

interface InstrumentDrawerProps {
  /** The open instrument; null keeps the drawer closed. */
  id: string | null;
  onClose: () => void;
}

/** Side panel with everything the master list knows about one instrument. */
export function InstrumentDrawer({ id, onClose }: InstrumentDrawerProps) {
  const query = useQuery({ ...detailQuery(id ?? ''), enabled: id !== null });
  const { data, isPending, isError, error } = query;

  return (
    <Drawer
      anchor="right"
      open={id !== null}
      onClose={onClose}
      slotProps={{ paper: { 'aria-label': 'Instrument details', sx: { width: { xs: '100%', sm: 460 } } } }}
    >
      <Stack direction="row" sx={{ p: 2, gap: 1, alignItems: 'flex-start', justifyContent: 'space-between' }}>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="h2" sx={{ fontFamily: monoFontFamily, overflowWrap: 'anywhere' }}>
            {data?.symbol ?? 'Instrument'}
          </Typography>
          {data && <Typography color="text.secondary">{data.displayName}</Typography>}
        </Box>
        <IconButton aria-label="Close details" edge="end" onClick={onClose}>
          <CloseIcon />
        </IconButton>
      </Stack>
      <Box sx={{ px: 2, pb: 3, overflowY: 'auto' }}>
        {id !== null && isPending && <LoadingState label="Loading instrument" />}
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
            {problemText(error, 'The instrument could not be loaded.')}
          </Alert>
        )}
        {data && (
          <Stack spacing={2}>
            <Flags detail={data} />
            <Facts items={detailFacts(data)} />
          </Stack>
        )}
      </Box>
    </Drawer>
  );
}
