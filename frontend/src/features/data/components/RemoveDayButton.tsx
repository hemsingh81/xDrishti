import { Alert, Button } from '@mui/material';
import { useState } from 'react';
import type { CalendarDay } from '@/shared/api';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { useToast } from '@/shared/ui/toastContext';
import { useRemoveCalendarDay } from '../api/calendarMutations';
import { dayName, isSpecial, removeVerb } from '../lib/calendar';
import { calendarErrorText } from '../lib/errors';

/** What the owner is told before it happens: the verb follows the API (own day removed, other days dismissed). */
function consequence(day: CalendarDay): string {
  const what = isSpecial(day) ? 'special session' : 'holiday';
  return day.source === 'manual'
    ? `The ${what} on ${dayName(day)} leaves your calendar and the date goes back to being an ordinary day.`
    : `The ${what} on ${dayName(day)} stays out of the calendar and is not proposed again, not even by the stored data or NSE's list.`;
}

/** "Remove" (own day) or "Dismiss" (NSE or observed day) for a confirmed day, behind a confirmation. */
export function RemoveDayButton({ day }: { day: CalendarDay }) {
  const verb = removeVerb(day);
  const { notify } = useToast();
  const remove = useRemoveCalendarDay();
  const [open, setOpen] = useState(false);

  const close = () => {
    setOpen(false);
    remove.reset();
  };
  const confirm = async () => {
    try {
      await remove.mutateAsync(day.date);
      setOpen(false);
      notify(`${verb === 'Remove' ? 'Removed' : 'Dismissed'} ${dayName(day)}.`);
    } catch {
      // The dialog shows the reason and stays open.
    }
  };

  return (
    <>
      <Button
        size="small"
        color="inherit"
        aria-label={`${verb} ${dayName(day)}`}
        onClick={() => {
          setOpen(true);
        }}
      >
        {verb}
      </Button>
      <ConfirmDialog
        open={open}
        title={`${verb} ${dayName(day)}?`}
        confirmLabel={verb}
        destructive
        busy={remove.isPending}
        onConfirm={() => {
          void confirm();
        }}
        onCancel={close}
      >
        {consequence(day)}
        {remove.isError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {calendarErrorText(remove.error, 'The day could not be changed.')}
          </Alert>
        )}
      </ConfirmDialog>
    </>
  );
}
