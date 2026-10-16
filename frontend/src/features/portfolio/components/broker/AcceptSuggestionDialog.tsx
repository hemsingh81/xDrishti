import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useState, type SyntheticEvent } from 'react';
import type { LedgerEntry } from '@/shared/api';
import { formatDate, formatInrPrecise } from '@/shared/lib/format';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { useAcceptSuggestion } from '../../api/dividendMutations';
import { maxNoteLength } from '../../lib/dividendForm';
import { fieldMessages, problemKind, problemText } from '../../lib/errors';
import { DividendHoldingField } from '../income/DividendHoldingField';

interface AcceptSuggestionDialogProps {
  accountId: string;
  entry: LedgerEntry;
  /** The dividend was added from the suggestion. */
  onAccepted: () => void;
  onClose: () => void;
}

/**
 * Accepting a suggested dividend of the ledger: the amount and the date are the entry's; the owner chooses the holding it is
 * for (or none: unallocated) and may add a note. Nothing is created without this step.
 */
export function AcceptSuggestionDialog({ accountId, entry, onAccepted, onClose }: AcceptSuggestionDialogProps) {
  const { notify } = useToast();
  const accept = useAcceptSuggestion(accountId);
  const [holdingId, setHoldingId] = useState('');
  const [note, setNote] = useState('');
  const noteError =
    note.length > maxNoteLength ? `The note can have at most ${String(maxNoteLength)} characters.` : null;
  const conflict = accept.isError && problemKind(accept.error) === 'conflict';
  // The API names the field it found fault with (the amount, the holding, the note): its words are the explanation. The
  // amount is the entry's and is shown at its own field, so a refusal of it (zero, negative or too large) sits right there.
  const fields = accept.isError && problemKind(accept.error) === 'invalid' ? fieldMessages(accept.error) : {};
  const { amount: amountError, ...otherFields } = fields;
  const fieldText = Object.values(otherFields).join(' ');

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    if (noteError) return;
    accept.mutate(
      {
        entryId: entry.id,
        body: { holdingId: holdingId === '' ? null : holdingId, note: note.trim() === '' ? null : note.trim() },
      },
      {
        onSuccess: (dividend) => {
          notify(`Added the dividend of ${formatInrPrecise(dividend.amount)} on ${formatDate(dividend.payDate)}.`);
          onAccepted();
          onClose();
        },
      },
    );
  }

  return (
    <AppDialog open onClose={onClose} busy={accept.isPending} maxWidth="sm" fullScreenOnMobile onSubmit={submit}>
      <AppDialogHeader subtitle={`${formatInrPrecise(entry.amount)} on ${formatDate(entry.date)}`}>
        Accept the suggested dividend
      </AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2.5}>
          <Typography color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
            {entry.description}
            {entry.narration ? ` — ${entry.narration}` : ''}
          </Typography>
          <TextField
            label="Amount"
            value={formatInrPrecise(entry.amount)}
            error={amountError !== undefined}
            helperText={amountError ?? 'This is the amount of the ledger entry; it cannot be changed here.'}
            slotProps={{ htmlInput: { readOnly: true } }}
          />
          <DividendHoldingField accountId={accountId} value={holdingId} onChange={setHoldingId} />
          <TextField
            label="Note (optional)"
            value={note}
            error={noteError !== null}
            helperText={noteError ?? `${String(note.length)} of ${String(maxNoteLength)} characters`}
            multiline
            maxRows={3}
            onChange={(event) => {
              setNote(event.target.value);
              accept.reset();
            }}
          />
          {accept.isError && !(amountError !== undefined && fieldText === '') && (
            <Alert severity={conflict ? 'warning' : 'error'} role="alert">
              {conflict
                ? 'This suggestion was decided meanwhile. The list is up to date now.'
                : fieldText || problemText(accept.error, 'The dividend could not be added. Nothing was changed.')}
            </Alert>
          )}
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={accept.isPending}>
          {conflict ? 'Close' : 'Cancel'}
        </Button>
        <Button type="submit" variant="contained" loading={accept.isPending} disabled={conflict}>
          Add as a dividend
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
