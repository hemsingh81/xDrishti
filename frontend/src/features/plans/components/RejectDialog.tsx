import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useRef, useState } from 'react';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { noOrderWording } from '../lib/planText';

interface Props {
  symbol: string;
  busy: boolean;
  /** What went wrong with the last try; the dialog stays open and keeps what was typed. */
  error: string | null;
  onSubmit: (reason: string) => void;
  onClose: () => void;
}

/** Reject one ticket with a reason of 1 to 500 characters. */
export function RejectDialog({ symbol, busy, error, onSubmit, onClose }: Props) {
  const [reason, setReason] = useState('');
  const ref = useRef<HTMLInputElement>(null);
  const ok = reason.trim().length >= 1;
  return (
    <AppDialog
      open
      onClose={onClose}
      busy={busy}
      initialFocusRef={ref}
      onSubmit={(e) => {
        e.preventDefault();
        if (busy) return;
        if (ok) onSubmit(reason.trim());
      }}
    >
      <AppDialogHeader>{`Reject ${symbol}`}</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={1.5}>
          {error && <Alert severity="error">{error}</Alert>}
          <TextField
            label="Reason (required, up to 500 characters)"
            value={reason}
            onChange={(e) => {
              setReason(e.target.value);
            }}
            inputRef={ref}
            required
            multiline
            minRows={2}
            slotProps={{ htmlInput: { maxLength: 500 } }}
          />
          <Typography variant="caption" color="text.secondary">
            {noOrderWording}
          </Typography>
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={busy}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" color="error" disabled={!ok} loading={busy}>
          Reject ticket
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
