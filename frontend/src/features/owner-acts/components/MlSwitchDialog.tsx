import { Alert, Button, DialogContentText, Stack, TextField } from '@mui/material';
import { useRef, useState, type SyntheticEvent } from 'react';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useMlSwitchAct, type MlSwitchField } from '../api/mlActsApi';
import { mlProblem } from './mlProblem';

const maxReason = 500;

export type MlSwitchKey = 'influence' | 'autoPromote' | 'autoRollback' | 'autoHoldOutScore';

const fields: Record<MlSwitchKey, { field: MlSwitchField; title: string; name: string }> = {
  influence: { field: 'influenceEnabled', title: 'model influence', name: 'ml.influence.enabled' },
  autoPromote: { field: 'autoPromote', title: 'automatic promotion', name: 'ml.auto_promote' },
  autoRollback: { field: 'autoRollback', title: 'automatic rollback', name: 'ml.auto_rollback' },
  autoHoldOutScore: { field: 'autoHoldOutScore', title: 'automatic hold-out score', name: 'ml.holdout.auto_score' },
};

interface MlSwitchDialogProps {
  switchKey: MlSwitchKey;
  /** The position to change to. */
  on: boolean;
  onClose: () => void;
}

/**
 * Turns one ML switch ON or OFF as the owner. The password is asked only where the API demands it: turning influence or automatic
 * promotion ON, checked on every such change. It lives in this field only and is cleared after every try, right or wrong; a wrong
 * one is an answer, not a sign-out. No switch places an order.
 */
export function MlSwitchDialog({ switchKey, on, onClose }: MlSwitchDialogProps) {
  const act = useMlSwitchAct();
  const { field, title, name } = fields[switchKey];
  const askPassword = switchKey === 'autoHoldOutScore' || (on && switchKey !== 'autoRollback');
  const [reason, setReason] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const reasonRef = useRef<HTMLInputElement>(null);
  const ready =
    reason.trim().length > 0 &&
    reason.length <= maxReason &&
    (!askPassword || password.length > 0) &&
    !busy &&
    done === null;

  const submit = async (event: SyntheticEvent) => {
    event.preventDefault();
    if (!ready) return;
    setBusy(true);
    setProblem(null);
    try {
      const outcome = await act({ field, on, reason, ...(askPassword ? { password } : {}) });
      setDone(
        outcome.changed.length === 0
          ? `Nothing changed: ${name} was already ${on ? 'ON' : 'OFF'}.`
          : `Saved: ${name} is now ${on ? 'ON' : 'OFF'}. The change is recorded with your reason.`,
      );
    } catch (error) {
      setProblem(mlProblem(error));
      (askPassword ? passwordRef : reasonRef).current?.focus();
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
      initialFocusRef={reasonRef}
      onSubmit={(e) => void submit(e)}
    >
      <AppDialogHeader>{`Turn ${title} ${on ? 'ON' : 'OFF'}`}</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <DialogContentText component="div">
            {on
              ? `This switches ${name} ON. It opens nothing alone and places no order.`
              : `This switches ${name} OFF. It lowers reliance on ML and needs no password.`}
          </DialogContentText>
          {done === null && (
            <>
              <TextField
                label="Reason"
                value={reason}
                inputRef={reasonRef}
                onChange={(event) => {
                  setReason(event.target.value);
                }}
                required
                error={reason.length > maxReason}
                helperText={`${reason.length} of ${maxReason} characters`}
              />
              {askPassword && (
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
                  helperText="Asked again for every such change. A wrong one counts toward the sign-in lock."
                />
              )}
            </>
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
            {`Turn ${title} ${on ? 'ON' : 'OFF'}`}
          </Button>
        )}
      </AppDialogFooter>
    </AppDialog>
  );
}
