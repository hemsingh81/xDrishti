import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Typography } from '@mui/material';
import { Suspense, useEffect, useId, useState } from 'react';
import { PasswordReasonDialog } from '@/features/execution';
import { ApiError } from '@/shared/api';
import { useKillAct } from '../api/killActsApi';
import {
  actConfirm,
  actMeaning,
  actTitle,
  askedPassword,
  reasonLimit,
  toAct,
  type KillTarget,
} from '../lib/killActText';

// shortcut: the execution feature's `problemText` is not exported (it would add an eager re-export); same reading of the problem.
const problem = (error: unknown): string =>
  error instanceof ApiError ? (error.problem?.detail ?? error.problem?.title ?? error.message) : 'The request failed.';

interface Props {
  target: KillTarget;
  onDone: (message: string) => void;
  onClose: () => void;
}

/**
 * Confirms one kill act. Dismiss, re-enable and the automatic setting ask for a fresh password and a reason (the shared dialog);
 * apply asks for a plain confirmation. The server's refusal is shown as sent and the dialog stays open; a rejected password is
 * not kept (the mutation is dropped and the dialog remounted).
 */
export function KillActDialog({ target, onDone, onClose }: Props) {
  const id = useId();
  const act = useKillAct();
  const [refusal, setRefusal] = useState<unknown>(null);
  const [attempt, setAttempt] = useState(0);
  // The reason survives a refusal (only the password is dropped), so a wrong password costs one retype, not two.
  const [reason, setReason] = useState('');
  const { reset } = act;
  useEffect(() => reset, [reset]);

  const submit = (v: { password: string; reason: string }) => {
    setReason(v.reason);
    setRefusal(null);
    act.mutate(toAct(target, v), {
      onSuccess: (message) => {
        onDone(message);
      },
      onError: (error) => {
        setRefusal(error);
        reset();
        setAttempt((n) => n + 1);
      },
    });
  };
  const intro = (
    <>
      {actMeaning(target).map((line) => (
        <Typography key={line} variant="body2" sx={{ mb: 1 }}>
          {line}
        </Typography>
      ))}
    </>
  );

  if (askedPassword(target)) {
    return (
      <Suspense fallback={null}>
        <PasswordReasonDialog
          key={attempt}
          open
          title={actTitle(target)}
          confirmLabel={actConfirm(target)}
          pending={act.isPending}
          error={refusal}
          initialReason={reason}
          maxReasonLength={reasonLimit(target)}
          intro={intro}
          onSubmit={submit}
          onClose={onClose}
        />
      </Suspense>
    );
  }
  return (
    <Dialog open onClose={onClose} aria-labelledby={`${id}-title`} fullWidth maxWidth="sm">
      <DialogTitle id={`${id}-title`}>{actTitle(target)}</DialogTitle>
      <DialogContent>
        {intro}
        {Boolean(refusal) && (
          <Alert severity="error" sx={{ mt: 1 }}>
            {problem(refusal)}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={act.isPending}
          onClick={() => {
            submit({ password: '', reason: '' });
          }}
        >
          {actConfirm(target)}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
