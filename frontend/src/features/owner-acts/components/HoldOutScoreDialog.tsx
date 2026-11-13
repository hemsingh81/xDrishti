import { Alert, Button, DialogContentText, Stack, TextField } from '@mui/material';
import { useRef, useState, type SyntheticEvent } from 'react';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useHoldOutScoreAct } from '../api/mlActsApi';
import { mlProblem } from './mlProblem';

interface HoldOutScoreDialogProps {
  modelId: number;
  onClose: () => void;
}

/**
 * Scores one model on the locked hold-out as the owner (CF-10e part 3). The look is irreversible: the model family's one look at the
 * block is burned even if nothing can be scored. The password is typed for this act, kept in this field only and cleared after every
 * try. A refusal (409, 422, 401, 403) is shown as sent and nothing changed. It places no order and changes no switch.
 */
export function HoldOutScoreDialog({ modelId, onClose }: HoldOutScoreDialogProps) {
  const act = useHoldOutScoreAct();
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const ready = password.length > 0 && !busy && done === null;

  const submit = async (event: SyntheticEvent) => {
    event.preventDefault();
    if (!ready) return;
    setBusy(true);
    setProblem(null);
    try {
      const view = await act({ id: modelId, password });
      setDone(
        `Scored on ${String(view.rows)} hold-out candidates of the block from ${view.blockStart}. The look is taken and recorded as ${view.mode}.`,
      );
    } catch (error) {
      setProblem(mlProblem(error));
      passwordRef.current?.focus();
    } finally {
      setPassword('');
      setBusy(false);
    }
  };

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={busy}
      maxWidth="sm"
      initialFocusRef={passwordRef}
      onSubmit={(e) => void submit(e)}
    >
      <AppDialogHeader>{`Score model #${String(modelId)} on the hold-out`}</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <DialogContentText component="div">
            This takes the model family&apos;s one fresh look at the block in force and cannot be undone: the look stays
            burned even if nothing can be scored. It opens nothing alone and places no order.
          </DialogContentText>
          {done === null && (
            <TextField
              label="Your password for this act"
              type="password"
              autoComplete="current-password"
              value={password}
              inputRef={passwordRef}
              onChange={(event) => {
                setPassword(event.target.value);
              }}
              required
              helperText="Asked again for every look. A wrong one counts toward the sign-in lock."
            />
          )}
          {problem && <Alert severity="error">{problem}</Alert>}
          {done && <Alert severity="success">{done}</Alert>}
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={busy}>
          {done ? 'Done' : 'Cancel'}
        </Button>
        {done === null && (
          <Button type="submit" variant="contained" disabled={!ready} loading={busy}>
            Score on hold-out
          </Button>
        )}
      </AppDialogFooter>
    </AppDialog>
  );
}
