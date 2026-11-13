import { Alert, Button, Checkbox, FormControlLabel, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import { formatDate } from '@/shared/lib/format';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { holdOutKey, releaseHoldOutBlock, releaseProblem, type HoldOutBlock } from '../api/holdOutQueries';

/**
 * The owner's release of a hold-out block (HO-01, D4): the password is asked again for this act, cleared after every try and never kept.
 * A block with no fresh look is released only with the explicit forfeit, which the dialog says in words.
 */
export function ReleaseDialog({ block, onClose }: { block: HoldOutBlock; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const unlooked = block.state === 'sealed';
  const [password, setPassword] = useState('');
  const [forfeit, setForfeit] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const release = useMutation({
    gcTime: 0,
    mutationFn: (typed: string) => releaseHoldOutBlock(block.start, typed, unlooked && forfeit),
    onSuccess: () => {
      toast.notify('The block is released. It is development data now, labelled seen_holdout.', 'success');
      onClose();
    },
    onError: (error) => {
      setProblem(releaseProblem(error));
    },
    onSettled: () => {
      setPassword('');
      release.reset();
      void queryClient.invalidateQueries({ queryKey: holdOutKey });
    },
  });
  const ready = password.length > 0 && (!unlooked || forfeit) && !release.isPending;

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={release.isPending}
      initialFocusRef={passwordRef}
      maxWidth="xs"
      onSubmit={(event) => {
        event.preventDefault();
        if (!ready) return;
        setProblem(null);
        release.mutate(password);
      }}
    >
      <AppDialogHeader>{`Release the block from ${formatDate(block.start)}`}</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <Typography variant="body2">
            The block becomes development data, labelled seen_holdout, and every development cut moves forward. This
            cannot be undone.
          </Typography>
          {unlooked && (
            <Alert severity="warning">
              <Typography variant="body2" sx={{ mb: 1 }}>
                This block has no fresh look. Releasing it forfeits its looks for good: no strategy, model or system can
                ever look at it unseen.
              </Typography>
              <FormControlLabel
                control={
                  <Checkbox
                    checked={forfeit}
                    onChange={(event) => {
                      setForfeit(event.target.checked);
                    }}
                  />
                }
                label="Release it and forfeit its looks"
              />
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
            helperText="Asked again for every release. A wrong one counts toward the sign-in lock."
          />
          {problem !== null && <Alert severity="error">{problem}</Alert>}
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={release.isPending}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" disabled={!ready}>
          Release
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
