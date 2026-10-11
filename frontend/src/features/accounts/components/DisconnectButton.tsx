import LinkOffIcon from '@mui/icons-material/LinkOffOutlined';
import { Alert, Box, Button } from '@mui/material';
import { useState } from 'react';
import type { AccountDetail } from '@/shared/api';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { useToast } from '@/shared/ui/toastContext';
import { useDisconnect } from '../api/connectionMutations';
import { connectionProblem } from '../lib/connectErrors';
import { dhanApp } from '../lib/dhanApp';

interface DisconnectButtonProps {
  account: AccountDetail;
  /** Whether there is a token to delete. The dialog stays mounted either way, so it can finish after the status changed. */
  visible: boolean;
}

/** "Disconnect" with a confirmation: the stored token is deleted. */
export function DisconnectButton({ account, visible }: DisconnectButtonProps) {
  const { notify } = useToast();
  const disconnect = useDisconnect();
  const [open, setOpen] = useState(false);
  const message = connectionProblem(disconnect.error);

  function close() {
    setOpen(false);
    disconnect.reset();
  }

  return (
    <>
      {visible && (
        <Button
          variant="outlined"
          color="error"
          startIcon={<LinkOffIcon />}
          onClick={() => {
            setOpen(true);
          }}
        >
          Disconnect
        </Button>
      )}
      <ConfirmDialog
        open={open}
        title={`Disconnect ${account.name}?`}
        confirmLabel="Disconnect"
        destructive
        busy={disconnect.isPending}
        onCancel={close}
        onConfirm={() => {
          disconnect.mutate(account.id, {
            onSuccess: () => {
              setOpen(false);
              notify(`${account.name} was disconnected from ${dhanApp.name}.`);
            },
          });
        }}
      >
        <Box>
          The stored access token is deleted. Services cannot use <strong>{account.name}</strong> until you connect
          again. You will be asked for your password.
        </Box>
        {message && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {message}
          </Alert>
        )}
      </ConfirmDialog>
    </>
  );
}
