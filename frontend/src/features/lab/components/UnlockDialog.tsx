import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { confidenceProblem, unlockHoldOut } from '../api/confidenceQueries';
import { runKeys } from '../api/runQueries';

/** The owner's unlock (CF-06, D5): the password is asked again for this act, cleared after every try and never kept. */
export function UnlockDialog({
  runId,
  runReachesHoldOut,
  onClose,
}: {
  runId: string;
  runReachesHoldOut: boolean;
  onClose: () => void;
}) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [password, setPassword] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const unlock = useMutation({
    gcTime: 0,
    mutationFn: (typed: string) => unlockHoldOut(runId, typed),
    onSuccess: () => {
      toast.notify('The hold-out was opened once and the result is stored.', 'success');
      onClose();
    },
    onError: (error) => {
      setProblem(confidenceProblem(error, 'The hold-out was not opened.'));
    },
    onSettled: () => {
      setPassword('');
      unlock.reset();
      // A success stored a look; a 409 `lab.holdout_used` means one is stored already: read the report again either way.
      void queryClient.invalidateQueries({ queryKey: runKeys.reports });
      void queryClient.invalidateQueries({ queryKey: ['lab', 'ledger'] });
    },
  });
  const ready = password.length > 0 && !unlock.isPending;

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={unlock.isPending}
      initialFocusRef={passwordRef}
      maxWidth="xs"
      onSubmit={(event) => {
        event.preventDefault();
        if (!ready) return;
        setProblem(null);
        unlock.mutate(password);
      }}
    >
      <AppDialogHeader>Unlock the hold-out</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <Typography variant="body2">
            This runs the frozen plan once over the hold-out and stores the result. It cannot be undone, and a second
            look by this version is refused. A look at a block that was seen before is shown but cannot count toward a
            pass.
          </Typography>
          {runReachesHoldOut && (
            <Alert severity="warning">
              This run&apos;s window reached the block: this look can never count toward a pass, and it spends the
              block&apos;s one fresh look.
            </Alert>
          )}
          <TextField
            inputRef={passwordRef}
            label="Your password for this act"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => {
              setPassword(event.target.value);
            }}
            required
            helperText="Asked again for every unlock. A wrong one counts toward the sign-in lock."
          />
          {problem !== null && <Alert severity="error">{problem}</Alert>}
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={unlock.isPending}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" disabled={!ready}>
          Unlock
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
