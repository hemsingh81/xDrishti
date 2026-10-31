import { Alert, Button, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import { isStepUpCancelled, useStepUp } from '@/features/auth';
import { formatNumber } from '@/shared/lib/format';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { problemText } from '../api/confirmApi';
import { blotterQuery, positionsQuery } from '../api/viewsApi';
import { useStartHalt } from '../api/writesApi';

type Level = 'entries' | 'cancel' | 'exit_all';
const level3 = 'Stop new orders, cancel pending ones and exit all positions with marketable limit orders';

interface Props {
  open: boolean;
  onClose: () => void;
  onRequested: (level: Level) => void;
}

/**
 * The stop dialog (spec P12-10 criterion 8). Three outcome-labelled actions, each with its numbers; focus starts on Cancel.
 * The first two send the request at once; exit-all first lists the positions and needs the step-up and a second click.
 */
export function StopDialog({ open, onClose, onRequested }: Props) {
  const working = useQuery({ ...blotterQuery({ stateGroup: 'working' }), enabled: open });
  const positions = useQuery({ ...positionsQuery(), enabled: open });
  const start = useStartHalt();
  const { withStepUp } = useStepUp();
  const [confirmingExit, setConfirmingExit] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);

  const orders = working.data ? formatNumber(working.data.length) : 'not available';
  const open_ = positions.data?.filter((p) => p.netQuantity !== 0);
  const held = open_ ? formatNumber(open_.length) : 'not available';

  const send = async (level: Level) => {
    setFailure(null);
    const request = () =>
      start.mutateAsync({
        scope: 'system',
        accountId: null,
        level,
        reason: `Stopped from the app bar (${level})`,
        brokerKillSwitch: null,
      });
    try {
      await (level === 'exit_all' ? withStepUp(request)() : request());
      setConfirmingExit(false);
      onRequested(level);
      onClose();
    } catch (error) {
      if (!isStepUpCancelled(error)) setFailure(problemText(error));
    }
  };
  const close = () => {
    setConfirmingExit(false);
    setFailure(null);
    onClose();
  };

  return (
    <AppDialog open={open} onClose={close} busy={start.isPending} initialFocusRef={cancelRef}>
      <AppDialogHeader>Stop trading</AppDialogHeader>
      <AppDialogBody>
        {confirmingExit ? (
          <Stack sx={{ gap: 1.5 }}>
            <Typography>
              Every open position is exited with a marketable limit order. The system never sends market orders, so a
              fill is not guaranteed.
            </Typography>
            <ul aria-label="Positions to exit">
              {open_?.map((p) => (
                <li key={`${p.accountId}:${p.symbol}`}>
                  {p.symbol} ({p.account}): {p.netQuantity}
                </li>
              ))}
            </ul>
            {open_?.length === 0 && <Typography>There are no open positions.</Typography>}
            <Typography variant="body2">Your password is asked for next.</Typography>
          </Stack>
        ) : (
          <Stack sx={{ gap: 2 }}>
            <Typography variant="body2" sx={{ color: 'text.secondary' }}>
              Working orders: {orders}. Open positions: {held}. Stops stay at the broker whatever you choose.
            </Typography>
            <Button variant="outlined" onClick={() => void send('entries')} disabled={start.isPending}>
              Stop new orders
            </Button>
            <Button variant="outlined" onClick={() => void send('cancel')} disabled={start.isPending}>
              Stop new orders and cancel pending ones
            </Button>
            <Button
              variant="outlined"
              color="error"
              onClick={() => {
                setConfirmingExit(true);
              }}
            >
              {level3}
            </Button>
          </Stack>
        )}
        {failure && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {failure}
          </Alert>
        )}
      </AppDialogBody>
      <AppDialogFooter>
        <Button
          ref={cancelRef}
          onClick={
            confirmingExit
              ? () => {
                  setConfirmingExit(false);
                }
              : close
          }
        >
          {confirmingExit ? 'Back' : 'Cancel'}
        </Button>
        {confirmingExit && (
          <Button variant="contained" color="error" onClick={() => void send('exit_all')} disabled={start.isPending}>
            Exit all positions
          </Button>
        )}
      </AppDialogFooter>
    </AppDialog>
  );
}
