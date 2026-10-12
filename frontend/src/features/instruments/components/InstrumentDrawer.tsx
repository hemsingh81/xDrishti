import CloseIcon from '@mui/icons-material/Close';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Alert, Box, Button, Chip, Drawer, IconButton, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { AddToBasketDialog, InstrumentBaskets } from '@/features/baskets';
import type { InstrumentDetail } from '@/shared/api';
import { Facts, LoadingState } from '@/shared/ui';
import { monoFontFamily } from '@/theme';
import { detailQuery } from '../api/instrumentsQueries';
import { problemText } from '../lib/errors';
import { detailFacts } from '../lib/detailFacts';
import { typeLabel } from '../lib/labels';
import { trackingFacts } from '../lib/trackingFacts';
import { TrackToggle } from './TrackToggle';

function Flags({ detail }: { detail: InstrumentDetail }) {
  return (
    <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 0.75 }}>
      <Chip size="small" variant="outlined" label={typeLabel(detail.type)} />
      <Chip size="small" variant="outlined" label={detail.sector ?? 'No sector'} />
      {detail.fnoEligible && <Chip size="small" variant="outlined" color="info" label="F&O" />}
      {detail.asmGsm && <Chip size="small" variant="outlined" color="warning" label="ASM/GSM" />}
      {detail.tracked.active && <Chip size="small" variant="outlined" color="success" label="Tracked" />}
      {detail.tracked.retiredButTracked && (
        <Chip size="small" variant="outlined" color="warning" icon={<WarningIcon />} label="No longer in Dhan's list" />
      )}
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
  const [addingToBasket, setAddingToBasket] = useState(false);

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
            <Box>
              <Typography variant="h3" component="h3" sx={{ mb: 1 }}>
                Tracking
              </Typography>
              <Stack spacing={1.5}>
                <Facts items={trackingFacts(data.tracked)} />
                <Box>
                  <TrackToggle
                    id={data.id}
                    symbol={data.symbol}
                    tracked={data.tracked.active}
                    retired={data.retiredAt !== null}
                  />
                </Box>
              </Stack>
            </Box>
            <Box>
              <Typography variant="h3" component="h3" sx={{ mb: 1 }}>
                Baskets
              </Typography>
              <Stack spacing={1.5}>
                <InstrumentBaskets instrumentId={data.id} />
                <Box>
                  <Button
                    size="small"
                    variant="outlined"
                    disabled={data.retiredAt !== null && !data.tracked.active}
                    onClick={() => {
                      setAddingToBasket(true);
                    }}
                  >
                    Add to basket
                  </Button>
                </Box>
              </Stack>
            </Box>
            <Facts items={detailFacts(data)} />
          </Stack>
        )}
      </Box>
      {data && addingToBasket && (
        <AddToBasketDialog
          instrumentIds={[data.id]}
          label={data.symbol}
          onClose={() => {
            setAddingToBasket(false);
          }}
          onAdded={() => {
            setAddingToBasket(false);
          }}
        />
      )}
    </Drawer>
  );
}
