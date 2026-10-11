import { Alert, Box, Button, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import type { AccountDetail } from '@/shared/api';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { useToast } from '@/shared/ui/toastContext';
import { useArchiveAccount } from '../api/accountMutations';
import { problemMessage } from '../lib/fieldErrors';

/** "Remove account" (archive) with a confirmation; the password dialog follows when the API asks for it. */
export function RemoveAccount({ account, onRemoved }: { account: AccountDetail; onRemoved: () => void }) {
  const { notify } = useToast();
  const archive = useArchiveAccount();
  const [open, setOpen] = useState(false);
  const message = problemMessage(archive.error);

  function close() {
    setOpen(false);
    archive.reset();
  }

  return (
    <Stack spacing={1} sx={{ alignItems: 'flex-start' }}>
      <Typography variant="h3">Remove account</Typography>
      <Typography variant="body2" color="text.secondary">
        Services stop using this account; its history and audit trail are kept. The primary Data account cannot be
        removed — make another account primary first.
      </Typography>
      <Button
        color="error"
        variant="outlined"
        onClick={() => {
          setOpen(true);
        }}
      >
        Remove account
      </Button>
      <ConfirmDialog
        open={open}
        title={`Remove ${account.name}?`}
        confirmLabel="Remove account"
        destructive
        busy={archive.isPending}
        onCancel={close}
        onConfirm={() => {
          archive.mutate(account.id, {
            onSuccess: () => {
              setOpen(false);
              notify(`${account.name} was removed.`);
              onRemoved();
            },
          });
        }}
      >
        <Box>
          Services will stop using <strong>{account.name}</strong>. Its history is kept. You will be asked for your
          password.
        </Box>
        {message && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {message}
          </Alert>
        )}
      </ConfirmDialog>
    </Stack>
  );
}
