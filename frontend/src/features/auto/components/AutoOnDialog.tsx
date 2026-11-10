import { Alert, Button, DialogContentText, Stack, TextField } from '@mui/material';
import { useRef, useState, type SyntheticEvent } from 'react';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useAutoSwitch } from '../api/autoApi';
import { autoProblem } from '../lib/problem';

/** Says what ON does and that no order is placed, then asks for the password. The password lives in this field only and is cleared after every try. */
export function AutoOnDialog({ onClose }: { onClose: () => void }) {
  const turn = useAutoSwitch();
  const [password, setPassword] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const ready = password.length > 0 && !turn.isPending;

  const submit = async (event: SyntheticEvent) => {
    event.preventDefault();
    if (!ready) return;
    setProblem(null);
    try {
      await turn.mutateAsync({ on: true, password });
      onClose();
    } catch (error) {
      setProblem(autoProblem(error));
      passwordRef.current?.focus();
    } finally {
      setPassword('');
    }
  };

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={turn.isPending}
      maxWidth="sm"
      initialFocusRef={passwordRef}
      onSubmit={(e) => void submit(e)}
    >
      <AppDialogHeader>Turn Auto ON</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <DialogContentText component="div">
            Turning Auto ON resumes the nightly chain: data refresh, simulation, training and the plan. Each night it
            prepares suggestions for the next session with reasons. It only suggests: no order is placed, nothing is
            approved or armed. It will not turn on until the charge sheet is confirmed and every other precondition
            passes.
          </DialogContentText>
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
            helperText="Asked again every time Auto is turned ON. A wrong one counts toward the sign-in lock."
          />
          {problem && <Alert severity="error">{problem}</Alert>}
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={turn.isPending}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" disabled={!ready} loading={turn.isPending}>
          Turn Auto ON
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
