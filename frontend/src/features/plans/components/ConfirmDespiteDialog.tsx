import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useRef, useState } from 'react';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import type { Confirmation, ReviewAccount } from '../api/planQueries';
import { confirmableCodes } from '../lib/checks';
import { noOrderWording } from '../lib/planText';

interface Props {
  symbol: string;
  accounts: ReviewAccount[];
  names: Record<string, string>;
  busy: boolean;
  /** What went wrong with the last try; the dialog stays open and keeps what was typed. */
  error: string | null;
  onConfirm: (confirmations: Confirmation[]) => void;
  onClose: () => void;
}

/** Approve despite checks that are not a pass: exact codes per account, one required reason of 1 to 200 characters. */
export function ConfirmDespiteDialog({ symbol, accounts, names, busy, error, onConfirm, onClose }: Props) {
  const [reason, setReason] = useState('');
  const reasonRef = useRef<HTMLInputElement>(null);
  const rows = accounts.map((a) => ({ account: a, codes: confirmableCodes(a) })).filter((r) => r.codes.length > 0);
  const valid = reason.trim().length >= 1 && reason.trim().length <= 200 && rows.length > 0;

  const submit = () => {
    if (!valid || busy) return;
    onConfirm(rows.map((r) => ({ accountId: r.account.accountId, codes: r.codes, reason: reason.trim() })));
  };

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={busy}
      initialFocusRef={reasonRef}
      onSubmit={(e) => {
        e.preventDefault();
        submit();
      }}
    >
      <AppDialogHeader>{`Confirm despite: ${symbol}`}</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={1.5}>
          {error && <Alert severity="error">{error}</Alert>}
          <Typography>
            You approve {symbol} although these checks are not a pass. Each one is recorded with your reason.
          </Typography>
          <Stack component="ul" spacing={0.5} sx={{ m: 0, pl: 2 }} aria-label="Checks approved despite">
            {rows.map((r) => (
              <li key={r.account.accountId}>
                <strong>{names[r.account.accountId] ?? r.account.accountId}</strong> ({r.account.mode}):{' '}
                {r.codes.join(', ')}
              </li>
            ))}
          </Stack>
          <TextField
            label="Reason (required, 1 to 200 characters)"
            value={reason}
            onChange={(e) => {
              setReason(e.target.value);
            }}
            inputRef={reasonRef}
            multiline
            minRows={2}
            required
            slotProps={{ htmlInput: { maxLength: 200 } }}
            helperText={`${reason.length}/200`}
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
        <Button type="submit" variant="contained" disabled={!valid} loading={busy}>
          Approve despite these checks
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
