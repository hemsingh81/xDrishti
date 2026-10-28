import { Alert, Button, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import { api, unwrap } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { rampKey } from '../api/rampQueries';
import { stepNames, writeProblem } from '../lib/rampLabels';

/**
 * A write of the live feature (spec P11-04): sets the ramp step of one account (S1 first, or lower). The password is typed again for THIS move and lives in the
 * variables of the call only; the field is cleared after every try. Nothing here calls a broker or places an order.
 */
export function RampDialog({
  accountId,
  accountName,
  step,
  onClose,
}: {
  accountId: string;
  accountName: string;
  step: number | null;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const options = step === null ? [1] : [1, 2, 3].filter((s) => s < step);
  const [toStep, setToStep] = useState(options[0] ?? 1);
  const [reason, setReason] = useState('');
  const [password, setPassword] = useState('');
  const [touched, setTouched] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const passwordRef = useRef<HTMLInputElement>(null);
  const save = useMutation({
    gcTime: 0,
    mutationFn: async (input: { password: string }) =>
      unwrap(
        await api.POST('/api/live-trading/ramp/steps', {
          body: { accountId, toStep, reason: reason.trim(), password: input.password },
        }),
      ),
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
      <AppDialogHeader>{step === null ? 'Set the first ramp step' : 'Lower the ramp step'}</AppDialogHeader>
      <AppDialogBody>
        {saved ? (
          <Alert severity="success">Saved. It takes effect on the next plan; open tickets are untouched.</Alert>
        ) : (
          <Stack sx={{ gap: 2, pt: 1 }}>
            <Typography variant="body2">
              {accountName}: the live account trades at a fraction of its profile&apos;s risk. Orders are still yours to
              place by hand.
            </Typography>
            <TextField
              select
              label="Step"
              value={toStep}
              onChange={(e) => {
                setToStep(Number(e.target.value));
              }}
            >
              {options.map((s) => (
                <MenuItem key={s} value={s}>
                  {stepNames[s]}
                </MenuItem>
              ))}
            </TextField>
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
              label="Your password for this move"
              type="password"
              autoComplete="current-password"
              value={password}
              inputRef={passwordRef}
              onChange={(e) => {
                setPassword(e.target.value);
              }}
              required
              error={touched && password.length === 0}
              helperText={touched && password.length === 0 ? 'Type your password.' : 'Asked again for every move.'}
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
            Save step
          </Button>
        )}
      </AppDialogFooter>
    </AppDialog>
  );
}
