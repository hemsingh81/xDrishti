import { Button, Stack, TextField } from '@mui/material';
import { useEffect, useId, useRef, useState, type SyntheticEvent } from 'react';
import type { LotRow } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { useUpdateLotDate } from '../api/portfolioMutations';
import { minAcquiredOn, todayIso, validateAcquiredOn } from '@/shared/lib/dates';
import { fieldMessages, problemText } from '../lib/errors';

interface LotDateEditorProps {
  holdingId: string;
  lot: LotRow;
  /** Names the field for assistive technology, e.g. "1 Jan 2025". */
  lotName: string;
  onDone: () => void;
}

/** Inline editor for one lot's acquisition date: checked here (a real day, not before 1990, not in the future) and again by the API. */
export function LotDateEditor({ holdingId, lot, lotName, onDone }: LotDateEditorProps) {
  const { notify } = useToast();
  const update = useUpdateLotDate(holdingId);
  const [value, setValue] = useState(lot.acquiredOn);
  const [clientError, setClientError] = useState<string | undefined>(undefined);
  const errorId = useId();
  // The owner just asked to edit: the field is where the keyboard goes next.
  const field = useRef<HTMLInputElement>(null);
  useEffect(() => {
    field.current?.focus();
  }, []);
  const serverError = update.isError
    ? (fieldMessages(update.error).acquiredOn ?? problemText(update.error, 'The date could not be saved.'))
    : undefined;
  const error = clientError ?? serverError;

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    event.stopPropagation();
    const invalid = validateAcquiredOn(value);
    setClientError(invalid);
    if (invalid) return;
    update.mutate(
      { lotId: lot.id, acquiredOn: value },
      {
        onSuccess: () => {
          notify('Acquisition date updated.');
          onDone();
        },
      },
    );
  }

  return (
    <Stack component="form" noValidate onSubmit={submit} direction="row" spacing={1} sx={{ alignItems: 'flex-start' }}>
      <TextField
        inputRef={field}
        size="small"
        type="date"
        value={value}
        error={error !== undefined}
        helperText={error}
        onChange={(event) => {
          setValue(event.target.value);
          setClientError(undefined);
          update.reset();
        }}
        slotProps={{
          htmlInput: {
            'aria-label': `Acquired on, lot of ${lotName}`,
            'aria-describedby': error ? errorId : undefined,
            min: minAcquiredOn,
            max: todayIso(),
          },
          formHelperText: { id: errorId },
        }}
        sx={{ minWidth: 150 }}
      />
      <Button type="submit" size="small" variant="contained" loading={update.isPending} sx={{ mt: 0.25 }}>
        Save
      </Button>
      <Button size="small" onClick={onDone} disabled={update.isPending} sx={{ mt: 0.25 }}>
        Cancel
      </Button>
    </Stack>
  );
}
