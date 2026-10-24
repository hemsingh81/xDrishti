import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useRef, useState } from 'react';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import type { ReviewAccount } from '../api/planQueries';
import { noOrderWording } from '../lib/planText';

interface Props {
  symbol: string;
  accounts: ReviewAccount[];
  names: Record<string, string>;
  busy: boolean;
  /** What went wrong with the last try; the dialog stays open and keeps what was typed. */
  error: string | null;
  onSubmit: (reason: string, quantities: { accountId: string; quantity: number }[]) => void;
  onClose: () => void;
}

/** Size-down only: a smaller quantity for accounts the ticket is already sized for. Nothing else can be edited. */
export function ModifyDialog({ symbol, accounts, names, busy, error, onSubmit, onClose }: Props) {
  const [values, setValues] = useState<Record<string, string>>(
    Object.fromEntries(accounts.map((a) => [a.accountId, String(a.effectiveQuantity)])),
  );
  const [reason, setReason] = useState('');
  const firstRef = useRef<HTMLInputElement>(null);

  const parsed = accounts.map((a) => {
    const text = values[a.accountId] ?? '';
    const n = Number(text);
    const valid = /^\d+$/.test(text) && n >= 1 && n < a.effectiveQuantity;
    const unchanged = text === String(a.effectiveQuantity);
    return { account: a, n, valid, unchanged };
  });
  const changed = parsed.filter((p) => !p.unchanged);
  const allValid = changed.length > 0 && changed.every((p) => p.valid);
  const ok = allValid && reason.trim().length >= 1;

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={busy}
      initialFocusRef={firstRef}
      onSubmit={(e) => {
        e.preventDefault();
        if (busy) return;
        if (ok)
          onSubmit(
            reason.trim(),
            changed.map((p) => ({ accountId: p.account.accountId, quantity: p.n })),
          );
      }}
    >
      <AppDialogHeader>{`Lower the size: ${symbol}`}</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={1.5}>
          {error && <Alert severity="error">{error}</Alert>}
          <Typography>
            You can only lower the quantity (at least 1, below the current size). Stop, target and trigger cannot be
            edited; to trade differently, reject the ticket. The ticket becomes Modified, not approved.
          </Typography>
          {parsed.map((p, i) => {
            const name = names[p.account.accountId] ?? p.account.accountId;
            const bad = !p.unchanged && !p.valid;
            return (
              <TextField
                key={p.account.accountId}
                label={`Quantity for ${name} (${p.account.mode}, now ${p.account.effectiveQuantity})`}
                value={values[p.account.accountId]}
                onChange={(e) => {
                  setValues({ ...values, [p.account.accountId]: e.target.value });
                }}
                inputRef={i === 0 ? firstRef : undefined}
                error={bad}
                helperText={bad ? `Whole number from 1 to ${p.account.effectiveQuantity - 1}.` : undefined}
                slotProps={{ htmlInput: { inputMode: 'numeric' } }}
              />
            );
          })}
          <TextField
            label="Reason (required, up to 500 characters)"
            value={reason}
            onChange={(e) => {
              setReason(e.target.value);
            }}
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
        <Button type="submit" variant="contained" disabled={!ok} loading={busy}>
          Lower the size
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
