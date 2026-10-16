import { Button, Stack, TextField, Typography } from '@mui/material';
import { useRef, useState, type SyntheticEvent } from 'react';
import { isRealDay, todayIso } from '@/shared/lib/dates';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { fieldMessages, problemKind } from '../../lib/errors';
import { minTradeDate } from '../../lib/tradeForm';
import { ProblemNotice } from '../ProblemNotice';

interface SyncFromDialogProps {
  accountId: string;
  /** The account's name, for the dialog's subtitle. */
  accountName: string;
  /** Starts the read from a date; `onStarted` runs when the API took the run. */
  onStart: (options: { from: string }, onStarted: () => void) => void;
  /** The request is on its way. */
  busy: boolean;
  /** Why the last request was refused (a 400 on the field `from` is shown at the date). */
  error: unknown;
  onReset: () => void;
  onClose: () => void;
}

/** What is wrong with the date typed, before the API is asked (it checks again and has the last word). */
function dateError(value: string, today: string): string | null {
  if (value === '') return 'Enter the first date to read.';
  if (!isRealDay(value)) return 'Enter a valid date.';
  if (value < minTradeDate) return 'The date cannot be before 2 Jan 1990.';
  if (value > today) return 'The date cannot be in the future.';
  return null;
}

/**
 * Reads the account's trades and ledger from an earlier date than the one the read would start from (ADR 0026 §5). Dhan does
 * not say how far back its history goes: an earlier start re-reads and stores what Dhan returns, nothing is stored twice, and
 * if Dhan refuses the range or has nothing that old the run says so in plain words (in the lines of the trades and the ledger).
 * A start the API refuses (a date that leaves a hole after what was read) is shown at the date, in the API's words.
 */
export function SyncFromDialog({
  accountId,
  accountName,
  onStart,
  busy,
  error,
  onReset,
  onClose,
}: SyncFromDialogProps) {
  const [from, setFrom] = useState('');
  const [clientError, setClientError] = useState<string | null>(null);
  const fieldRef = useRef<HTMLInputElement>(null);
  const refusedField = problemKind(error) === 'invalid' ? (fieldMessages(error).from ?? null) : null;
  const text = clientError ?? refusedField;

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    const found = dateError(from, todayIso());
    setClientError(found);
    if (found) {
      fieldRef.current?.focus();
      return;
    }
    onStart({ from }, onClose);
  }

  return (
    <AppDialog open onClose={onClose} busy={busy} maxWidth="xs" initialFocusRef={fieldRef} onSubmit={submit}>
      <AppDialogHeader subtitle={accountName}>Read from an earlier date</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <Typography color="text.secondary">
            The trades and the ledger are read from the date you choose instead of from where the last read ended. Dhan
            does not say how far back its history goes: if it refuses the range or has nothing that old, the read says
            so. Days that were read before are read again; nothing is stored twice.
          </Typography>
          <TextField
            type="date"
            label="Read from"
            value={from}
            inputRef={fieldRef}
            error={text !== null}
            helperText={text ?? 'The first day to read'}
            onChange={(event) => {
              setFrom(event.target.value);
              setClientError(null);
              onReset();
            }}
            slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: minTradeDate, max: todayIso() } }}
          />
          {error && refusedField === null ? (
            <ProblemNotice error={error} accountId={accountId} fallback="The read did not start." />
          ) : null}
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={busy}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" loading={busy}>
          Read from this date
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
