import { Alert, Button, FormControlLabel, Stack, Switch, TextField, Typography } from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import { api, unwrap } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { liveSettingsKey, type LiveSettingsView } from '../api/rampQueries';
import { noOrderSentence, writeProblem } from '../lib/rampLabels';

/**
 * A write of the live feature (spec P11-04): changes the two live switches (both OFF unless you turn one on). The password is typed again for THIS change and
 * lives in the variables of the call only. Turning a switch on lets the rule approve (or raise) without you; it never lets this system place an order.
 */
export function LiveSettingsDialog({ current, onClose }: { current: LiveSettingsView; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [autoApprove, setAutoApprove] = useState(current.autoApproveTickets);
  const [autoRamp, setAutoRamp] = useState(current.autoRampUp);
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
        await api.PUT('/api/live-trading/settings', {
          body: {
            autoApproveTickets: autoApprove,
            autoRampUp: autoRamp,
            reason: reason.trim(),
            expectedSettingsId: current.id ?? 0,
            password: input.password,
          },
        }),
      ),
    onSettled: () => queryClient.invalidateQueries({ queryKey: liveSettingsKey }),
  });
  const reasonError =
    reason.trim().length === 0 ? 'Give a reason.' : reason.length > 500 ? 'At most 500 characters.' : undefined;
  const unchanged = autoApprove === current.autoApproveTickets && autoRamp === current.autoRampUp;

  const submit = async () => {
    setTouched(true);
    if (unchanged || reasonError || password.length === 0 || save.isPending) return;
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
      <AppDialogHeader>Live switches</AppDialogHeader>
      <AppDialogBody>
        {saved ? (
          <Alert severity="success">Saved as a new row of the history.</Alert>
        ) : (
          <Stack sx={{ gap: 2, pt: 1 }}>
            <Typography variant="body2">{noOrderSentence}</Typography>
            <FormControlLabel
              control={
                <Switch
                  checked={autoApprove}
                  onChange={(e) => {
                    setAutoApprove(e.target.checked);
                  }}
                />
              }
              label="Approve live tickets by rule (also needs the autonomy switch)"
            />
            <FormControlLabel
              control={
                <Switch
                  checked={autoRamp}
                  onChange={(e) => {
                    setAutoRamp(e.target.checked);
                  }}
                />
              }
              label="Raise S1 to S2 by rule (acts once the evidence gates exist)"
            />
            {touched && unchanged && <Alert severity="info">Nothing is changed.</Alert>}
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
              helperText={touched ? reasonError : undefined}
            />
            <TextField
              label="Your password for this change"
              type="password"
              autoComplete="current-password"
              value={password}
              inputRef={passwordRef}
              onChange={(e) => {
                setPassword(e.target.value);
              }}
              required
              error={touched && password.length === 0}
              helperText={touched && password.length === 0 ? 'Type your password.' : 'Asked again for every change.'}
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
            Save switches
          </Button>
        )}
      </AppDialogFooter>
    </AppDialog>
  );
}
