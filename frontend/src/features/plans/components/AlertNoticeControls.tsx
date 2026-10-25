import { Button, Stack } from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { dismissAlert, planKeys, snoozeAlert } from '../api/planQueries';

const minutesChoices = [15, 30, 60] as const;

interface Props {
  planId: number;
  noticeId: string;
  /** Names the notice for assistive technology, e.g. "stop crossed". */
  label: string;
  snoozesLeft: number;
  onError: (e: unknown) => void;
}

/** Owner acts on one alert or prompt: snooze 15/30/60 minutes, or dismiss. Neither places an order. */
export function AlertNoticeControls({ planId, noticeId, label, snoozesLeft, onError }: Props) {
  const queryClient = useQueryClient();
  const settle = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: planKeys.alerts(planId) }),
      queryClient.invalidateQueries({ queryKey: planKeys.notices }),
    ]);
  const snooze = useMutation({
    mutationFn: (minutes: 15 | 30 | 60) => snoozeAlert(noticeId, minutes),
    onSuccess: settle,
    onError,
  });
  const dismiss = useMutation({ mutationFn: () => dismissAlert(noticeId), onSuccess: settle, onError });
  const busy = snooze.isPending || dismiss.isPending;
  return (
    <Stack direction="row" role="group" aria-label={`Actions for ${label}`} sx={{ flexWrap: 'wrap', gap: 0.75 }}>
      {minutesChoices.map((m) => (
        <Button
          key={m}
          size="small"
          variant="outlined"
          disabled={busy || snoozesLeft <= 0}
          aria-label={`Snooze ${label} ${String(m)} minutes`}
          onClick={() => {
            snooze.mutate(m);
          }}
        >
          Snooze {m} min
        </Button>
      ))}
      <Button
        size="small"
        variant="outlined"
        disabled={busy}
        aria-label={`Dismiss ${label}`}
        onClick={() => {
          dismiss.mutate();
        }}
      >
        Dismiss
      </Button>
    </Stack>
  );
}
