import { Alert, Button, DialogContentText, Stack } from '@mui/material';
import { useRef, useState } from 'react';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useApplyRollbackAct } from '../api/mlActsApi';
import { mlProblem } from './mlProblem';

/**
 * Confirms applying one rollback proposal as the owner (session and CSRF; the API asks for no password). It moves the champion to
 * challenger and never re-promotes anyone: there is no champion until the owner names one. A refusal (409 with its code, 403) is
 * shown as sent and the dialog stays open; the result stays on screen until it is closed.
 */
export function ApplyRollbackDialog({ proposalId, onClose }: { proposalId: number; onClose: () => void }) {
  const act = useApplyRollbackAct();
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);

  const apply = async () => {
    setBusy(true);
    setProblem(null);
    try {
      const outcome = await act(proposalId);
      const moved = outcome.alreadyApplied
        ? 'It was applied before; nothing was changed now.'
        : outcome.demoted
          ? `Model #${outcome.modelId} moved from champion to challenger.`
          : `Model #${outcome.modelId} was not the champion any more; nothing was moved.`;
      setDone(`Proposal #${outcome.proposalId} is ${outcome.state}. ${moved} There is no champion until you name one.`);
    } catch (error) {
      setProblem(mlProblem(error));
    } finally {
      setBusy(false);
    }
  };

  return (
    <AppDialog open onClose={onClose} busy={busy} maxWidth="xs" initialFocusRef={cancelRef}>
      <AppDialogHeader>{`Apply rollback #${proposalId}`}</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <DialogContentText component="div">
            The champion moves to challenger (shadow), with a changelog row carrying the trigger, the numbers and the
            settings hash. It never re-promotes the previous champion: you name the next one yourself. It places no
            order and needs no password.
          </DialogContentText>
          {problem && <Alert severity="error">{problem}</Alert>}
          {done && <Alert severity="success">{done}</Alert>}
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={busy} ref={cancelRef}>
          {done ? 'Done' : 'Cancel'}
        </Button>
        {done === null && (
          <Button
            variant="contained"
            loading={busy}
            onClick={() => {
              void apply();
            }}
          >
            {`Yes, apply rollback #${proposalId}`}
          </Button>
        )}
      </AppDialogFooter>
    </AppDialog>
  );
}
