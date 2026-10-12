import { Button, Stack } from '@mui/material';
import type { CalendarDay } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { useConfirmCalendarDay, useRemoveCalendarDay } from '../api/calendarMutations';
import { dayName } from '../lib/calendar';
import { calendarErrorText } from '../lib/errors';

/** Confirm (the day becomes a holiday or special session) or Dismiss (it is not proposed again) for a proposal. */
export function ProposalActions({ day }: { day: CalendarDay }) {
  const { notify } = useToast();
  const confirm = useConfirmCalendarDay();
  const dismiss = useRemoveCalendarDay();
  const busy = confirm.isPending || dismiss.isPending;
  const name = dayName(day);

  // The row leaves the list when the reload ends, so the answers are handled here, not in the button's callbacks.
  const run = async (action: () => Promise<unknown>, done: string, failed: string) => {
    try {
      await action();
      notify(done);
    } catch (error) {
      notify(calendarErrorText(error, failed), 'error');
    }
  };

  return (
    <Stack direction="row" spacing={0.5} sx={{ justifyContent: 'flex-end' }}>
      <Button
        size="small"
        variant="outlined"
        aria-label={`Confirm ${name}`}
        loading={confirm.isPending}
        disabled={busy}
        onClick={() => {
          void run(
            () => confirm.mutateAsync(day.date),
            `Confirmed ${name}: it now counts as a ${day.kind === 'special' ? 'special session' : 'non-trading day'}.`,
            'The day could not be confirmed.',
          );
        }}
      >
        Confirm
      </Button>
      <Button
        size="small"
        color="inherit"
        aria-label={`Dismiss ${name}`}
        loading={dismiss.isPending}
        disabled={busy}
        onClick={() => {
          void run(
            () => dismiss.mutateAsync(day.date),
            `Dismissed ${name}; it will not be proposed again.`,
            'The day could not be dismissed.',
          );
        }}
      >
        Dismiss
      </Button>
    </Stack>
  );
}
