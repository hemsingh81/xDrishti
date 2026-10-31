import { Box, Button, Chip, Link as MuiLink, Stack } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router';
import { formatIstTime } from '@/shared/lib/format';
import { executionStatusQuery } from '../api/statusApi';
import { activeHaltsQuery } from '../api/viewsApi';
import { ReleaseDialog } from './ReleaseDialog';
import { StopDialog } from './StopDialog';

/**
 * The stop control in the app bar. Visible while anything is automated or a halt is active. It says "Trading stopped" only
 * when the halt row exists: a request alone is "Stop requested" (the halt is written by xd-exec, which may not be running).
 */
export function StopChip() {
  const status = useQuery(executionStatusQuery);
  const [requested, setRequested] = useState(false);
  const [dialog, setDialog] = useState<'stop' | 'release' | null>(null);
  const reading = status.data?.killSwitchActive === true || requested;
  const halts = useQuery({ ...activeHaltsQuery, enabled: reading, refetchInterval: requested ? 2000 : false });
  const halt = halts.data?.[0];

  // The request is over once its halt row is there (adjusting state while rendering, not in an effect).
  if (halt && requested) setRequested(false);

  if (halt) {
    return (
      <Stack direction="row" sx={{ alignItems: 'center', gap: 0.5 }}>
        <Chip
          color="error"
          size="small"
          role="status"
          label={`Trading stopped (halt ${halt.id}, ${halt.level}, ${formatIstTime(halt.at)} IST)`}
        />
        <MuiLink component={Link} to="/execution?tab=today" variant="caption">
          Details
        </MuiLink>
        <Button
          size="small"
          onClick={() => {
            setDialog('release');
          }}
        >
          Release
        </Button>
        <ReleaseDialog
          halt={halt}
          open={dialog === 'release'}
          onClose={() => {
            setDialog(null);
          }}
        />
      </Stack>
    );
  }
  if (requested) {
    return (
      <Chip
        color="warning"
        size="small"
        role="status"
        label={status.data?.service.state === 'down' ? 'Stop requested, xd-exec is not running' : 'Stop requested'}
      />
    );
  }
  if (status.data?.killSwitchActive) {
    return <Chip size="small" role="status" label="A halt is reported, reading it" />;
  }
  // Stopping is the safe direction: when the status cannot be read the stop button stays available.
  if (!status.isError && !status.data?.anyAutomated) return null;
  return (
    <Box>
      <Button
        size="small"
        color="error"
        variant="outlined"
        onClick={() => {
          setDialog('stop');
        }}
      >
        Stop trading
      </Button>
      <StopDialog
        open={dialog === 'stop'}
        onClose={() => {
          setDialog(null);
        }}
        onRequested={() => {
          setRequested(true);
        }}
      />
    </Box>
  );
}
