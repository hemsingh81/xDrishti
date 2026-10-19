import { Alert, Button, Stack, TextField } from '@mui/material';
import { unshownFieldErrorsText } from '@/shared/lib/fieldErrors';
import { useRef, useState, type SyntheticEvent } from 'react';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { useAddCalendarDay } from '../api/calendarMutations';
import { dayName, maxNoteLength } from '../lib/calendar';
import {
  emptyCalendarForm,
  toCalendarInput,
  validateCalendarForm,
  type CalendarForm,
  type CalendarKind,
} from '../lib/calendarForm';
import { calendarErrorText, fieldErrors } from '../lib/errors';

const shownFields = ['date', 'kind', 'sessionFrom', 'sessionTo', 'note'];

/**
 * Add a holiday (a weekday with no session) or a special session (a session on a weekend or holiday, or at another
 * time, with its window in India time). The server's messages show on their fields, e.g. a holiday on a weekend.
 */
export function AddCalendarDayDialog({ onClose }: { onClose: () => void }) {
  const { notify } = useToast();
  const add = useAddCalendarDay();
  const dateRef = useRef<HTMLInputElement>(null);
  const [form, setForm] = useState<CalendarForm>(emptyCalendarForm);
  const [clientErrors, setClientErrors] = useState<Record<string, string>>({});
  const serverErrors = fieldErrors(add.error);
  const errors = { ...serverErrors, ...clientErrors };
  const otherErrors = unshownFieldErrorsText(serverErrors, shownFields);
  const problem =
    add.isError && Object.keys(serverErrors).length === 0
      ? calendarErrorText(add.error, 'The day could not be added.')
      : null;
  const special = form.kind === 'special';

  const change = (patch: Partial<CalendarForm>) => {
    setForm((previous) => ({ ...previous, ...patch }));
    setClientErrors({});
    add.reset();
  };

  const submit = async (event: SyntheticEvent) => {
    event.preventDefault();
    const found = validateCalendarForm(form);
    setClientErrors(found);
    if (Object.keys(found).length > 0) return;
    try {
      const { day, replaced } = await add.mutateAsync(toCalendarInput(form));
      notify(`${replaced ? 'Replaced the entry for' : 'Added'} ${dayName(day)}.`);
      onClose();
    } catch {
      // The fields (or the alert) show what the server said.
    }
  };

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={add.isPending}
      initialFocusRef={dateRef}
      onSubmit={(event) => {
        void submit(event);
      }}
    >
      <AppDialogHeader>Add holiday / special session</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2.5}>
          {problem && <Alert severity="error">{problem}</Alert>}
          {otherErrors && <Alert severity="error">{otherErrors}</Alert>}
          <TextField
            required
            fullWidth
            type="date"
            label="Date"
            inputRef={dateRef}
            value={form.date}
            error={Boolean(errors.date)}
            helperText={errors.date}
            onChange={(event) => {
              change({ date: event.target.value });
            }}
            slotProps={{ inputLabel: { shrink: true } }}
          />
          <TextField
            select
            fullWidth
            label="Kind"
            value={form.kind}
            error={Boolean(errors.kind)}
            helperText={
              errors.kind ??
              (special
                ? 'A session on a weekend or holiday, or at another time (e.g. Muhurat trading).'
                : 'A weekday with no session.')
            }
            onChange={(event) => {
              change({ kind: event.target.value as CalendarKind });
            }}
            slotProps={{ select: { native: true } }}
          >
            <option value="holiday">Holiday</option>
            <option value="special">Special session</option>
          </TextField>
          {special && (
            <Stack direction="row" spacing={2}>
              <TextField
                fullWidth
                type="time"
                label="Session from"
                value={form.sessionFrom}
                error={Boolean(errors.sessionFrom)}
                helperText={errors.sessionFrom ?? 'India time'}
                onChange={(event) => {
                  change({ sessionFrom: event.target.value });
                }}
                slotProps={{ inputLabel: { shrink: true } }}
              />
              <TextField
                fullWidth
                type="time"
                label="Session to"
                value={form.sessionTo}
                error={Boolean(errors.sessionTo)}
                helperText={errors.sessionTo ?? 'India time, the end is not included'}
                onChange={(event) => {
                  change({ sessionTo: event.target.value });
                }}
                slotProps={{ inputLabel: { shrink: true } }}
              />
            </Stack>
          )}
          <TextField
            fullWidth
            label="Note"
            value={form.note}
            placeholder="e.g. Diwali"
            error={Boolean(errors.note)}
            helperText={errors.note ?? `${String(form.note.length)} / ${String(maxNoteLength)}`}
            onChange={(event) => {
              change({ note: event.target.value });
            }}
          />
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={add.isPending}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" loading={add.isPending}>
          Add
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
