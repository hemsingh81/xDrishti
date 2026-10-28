import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import { api, unwrap } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { rampKey } from '../api/rampQueries';
import { writeProblem } from '../lib/rampLabels';

const copy = {
  'clear-hold': {
    title: 'Clear the hold',
    body: 'A hold blocks raising the step. You can clear it once the breach conditions are gone; the system refuses while one still holds.',
    save: 'Clear hold',
  },
  'exit-confirmed': {
    title: 'Record the exit confirmation',
    body: 'You read the small-live exit report and it says it is met. S3 cannot be set without this. The system refuses it while the report cannot be read or is not met.',
    save: 'Record confirmation',
  },
} as const;

/**
 * The owner's two other ramp acts (spec P11-04): clear a hold, or record the exit confirmation. The password is typed again for THIS act and lives in the
 * variables of the call only; the field is cleared after every try. Nothing here calls a broker or places an order.
 */
export function RampActDialog({
  accountId,
  accountName,
  act,
  onClose,
}: {
  accountId: string;
  accountName: string;
  act: 'clear-hold' | 'exit-confirmed';
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const [reason, setReason] = useState('');
  const [password, setPassword] = useState('');
  const [touched, setTouched] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const passwordRef = useRef<HTMLInputElement>(null);
  const text = copy[act];
  const save = useMutation({
    gcTime: 0,
    mutationFn: async (input: { password: string }) => {
      const body = { accountId, reason: reason.trim(), password: input.password };
      return unwrap(
        act === 'clear-hold'
          ? await api.POST('/api/live-trading/ramp/clear-hold', { body })
          : await api.POST('/api/live-trading/ramp/exit-confirmed', { body }),
      );
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: rampKey }),
  });
  const reasonError =
    reason.trim().length === 0 ? 'Give a reason.' : reason.length > 300 ? 'At most 300 characters.' : undefined;

  const submit = async () => {
    setTouched(true);
    if (reasonError || password.length === 0 || save.isPending) return;
    setProblem(null);
    try {
      await save.mutateAsync({ password });
      setSaved(true);
    } catch (error) {
      setProblem(writeProblem(error));
      passwordRef.current?.focus();
    } finally {
      setPassword('');
      save.reset();
    }
  };

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={save.isPending}
      maxWidth="sm"
      fullScreenOnMobile
      onSubmit={(event) => {
        event.preventDefault();
        void submit();
      }}
    >
      <AppDialogHeader>{text.title}</AppDialogHeader>
      <AppDialogBody>
        {saved ? (
          <Alert severity="success">Saved. It takes effect on the next plan; open tickets are untouched.</Alert>
        ) : (
          <Stack sx={{ gap: 2, pt: 1 }}>
            <Typography variant="body2">
              {accountName}: {text.body} Orders are still yours to place by hand.
            </Typography>
            <TextField
              label="Reason"
              value={reason}
              onChange={(e) => {
                setReason(e.target.value);
              }}
              multiline
              minRows={2}
              required
              error={touched && Boolean(reasonError)}
              helperText={touched ? reasonError : 'Kept in the history of the step.'}
            />
            <TextField
              label="Your password for this act"
              type="password"
              autoComplete="current-password"
              value={password}
              inputRef={passwordRef}
              onChange={(e) => {
                setPassword(e.target.value);
              }}
              required
              error={touched && password.length === 0}
              helperText={touched && password.length === 0 ? 'Type your password.' : 'Asked again for every act.'}
            />
            {problem && <Alert severity="error">{problem}</Alert>}
          </Stack>
        )}
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={save.isPending}>
          {saved ? 'Close' : 'Cancel'}
        </Button>
        {!saved && (
          <Button type="submit" variant="contained" disabled={save.isPending}>
            {text.save}
          </Button>
        )}
      </AppDialogFooter>
    </AppDialog>
  );
}
