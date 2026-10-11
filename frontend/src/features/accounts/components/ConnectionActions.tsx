import LoginIcon from '@mui/icons-material/Login';
import { Button, Stack, Typography } from '@mui/material';
import { useId } from 'react';
import type { AccountDetail } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { useStartConnect } from '../api/connectionMutations';
import { connectionProblem } from '../lib/connectErrors';
import { appConfigured, connectLabel, type ConnectionStatusKey } from '../lib/connection';
import { dhanApp } from '../lib/dhanApp';
import { DisconnectButton } from './DisconnectButton';

interface ConnectionActionsProps {
  account: AccountDetail;
  /** The status against the clock (a passed expiry reads "expired"). */
  status: ConnectionStatusKey;
}

/** Connect / Reconnect (the Dhan sign-in, primary) and — once there is a token — Disconnect. */
export function ConnectionActions({ account, status }: ConnectionActionsProps) {
  const { notify } = useToast();
  const reasonId = useId();
  const connect = useStartConnect();
  const ready = appConfigured(account);

  return (
    <Stack spacing={0.75}>
      <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1 }}>
        <Button
          variant="contained"
          startIcon={<LoginIcon />}
          disabled={!ready}
          aria-describedby={ready ? undefined : reasonId}
          // After a successful start the browser is on its way to Dhan: keep the spinner until it leaves.
          loading={connect.isPending}
          onClick={() => {
            connect.mutate(account.id, {
              onError: (error) => {
                const message = connectionProblem(error);
                if (message) {
                  notify(message, 'error');
                }
              },
            });
          }}
        >
          {connectLabel(status)} with {dhanApp.name}
        </Button>
        <DisconnectButton account={account} visible={status !== 'not_connected'} />
      </Stack>
      {!ready && (
        <Typography id={reasonId} variant="body2" color="text.secondary">
          Store the API key and secret first (Broker app → Replace secrets).
        </Typography>
      )}
    </Stack>
  );
}
