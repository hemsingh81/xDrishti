import { Alert, Button, Checkbox, DialogContentText, FormControlLabel, Stack, TextField } from '@mui/material';
import { useRef, useState, type SyntheticEvent } from 'react';
import { ApiError } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useShadowSwitch } from '../api/shadowQueries';

const maxReason = 500;

/** The words of a refused shadow switch change; every one says nothing changed. */
function shadowProblem(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Could not reach the server. Nothing was changed.';
  const p = error.problem;
  if (error.status === 401) return 'The password was not accepted. Nothing was changed.';
  if (error.status === 423) return `Too many wrong passwords; sign-in is locked for now. ${p?.detail ?? ''}`.trim();
  if (error.status === 429) return 'Too many attempts. Wait a little and try again. Nothing was changed.';
  if (p?.code === 'paper.shadow_digest_unwritable')
    return 'The digest folder cannot be written, so the record cannot be switched on. Nothing was changed.';
  const text = [p?.title, p?.detail].filter(Boolean).join(' ') || 'The request failed.';
  return `${text}${p?.code ? ` (${p.code})` : ''} Nothing was changed.`;
}

/** Switch the shadow record ON or OFF: a reason and a fresh password, and, after a 409 gap, the owner's acknowledgement. The password is cleared after every try. */
export function ShadowSwitchDialog({ turnOn, onClose }: { turnOn: boolean; onClose: () => void }) {
  const change = useShadowSwitch();
  const [password, setPassword] = useState('');
  const [reason, setReason] = useState('');
  const [gap, setGap] = useState<string | null>(null);
  const [acknowledge, setAcknowledge] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const ready = password.length > 0 && reason.trim().length > 0 && reason.length <= maxReason && !change.isPending;
  const verb = turnOn ? 'Switch on' : 'Switch off';

  const submit = async (event: SyntheticEvent) => {
    event.preventDefault();
    if (!ready) return;
    setProblem(null);
    try {
      await change.mutateAsync({ enabled: turnOn, reason: reason.trim(), password, acknowledgeGap: acknowledge });
      onClose();
    } catch (error) {
      if (error instanceof ApiError && error.status === 409 && error.problem?.code === 'paper.shadow_gap') {
        setGap(error.problem.detail ?? error.problem.title ?? 'Sessions were missed since the chain last ended.');
      } else {
        setProblem(shadowProblem(error));
      }
      passwordRef.current?.focus();
    } finally {
      setPassword('');
    }
  };

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={change.isPending}
      maxWidth="sm"
      initialFocusRef={passwordRef}
      onSubmit={(e) => void submit(e)}
    >
      <AppDialogHeader>{verb}: shadow record</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <DialogContentText component="div">
            {turnOn
              ? 'Switching on lets the shadow-write and shadow-grade services work (they start enabled; the record is ON by default once an evidence set exists). The forward count cannot be rewound. No order is placed.'
              : 'Switching off stops the shadow-write and shadow-grade services. Rows already stored stay. No order is placed.'}
          </DialogContentText>
          <TextField
            label="Reason"
            value={reason}
            onChange={(event) => {
              setReason(event.target.value);
            }}
            required
            multiline
            slotProps={{ htmlInput: { maxLength: maxReason } }}
            helperText="Why, up to 500 characters. It is stored with the change."
          />
          <TextField
            label="Your password for this change"
            type="password"
            autoComplete="current-password"
            value={password}
            inputRef={passwordRef}
            onChange={(event) => {
              setPassword(event.target.value);
            }}
            required
            helperText="Asked again every time. A wrong one counts toward the sign-in lock."
          />
          {gap && (
            <>
              <Alert severity="warning">
                {gap} Acknowledging turns every missed session into a gap row; none is back-filled.
              </Alert>
              <FormControlLabel
                control={
                  <Checkbox
                    checked={acknowledge}
                    onChange={(event) => {
                      setAcknowledge(event.target.checked);
                    }}
                  />
                }
                label="I acknowledge the gap"
              />
            </>
          )}
          {problem && <Alert severity="error">{problem}</Alert>}
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={change.isPending}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" disabled={!ready} loading={change.isPending}>
          {verb}
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
