import { Alert, Button, DialogContentText, Stack, TextField } from '@mui/material';
import { useRef, useState, type SyntheticEvent } from 'react';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useStepUpMutation } from '../api/stepUpMutation';
import { describeSignInProblem } from '../lib/signInProblem';
import { RetryCountdown } from './RetryCountdown';

/**
 * Asks for the password again before a sensitive change. The password lives only in this component's state, which is
 * discarded when the dialog closes; it is sent to the API once and never stored or logged.
 */
export function StepUpDialog({ onDone }: { onDone: (accepted: boolean) => void }) {
  const stepUp = useStepUpMutation();
  const [password, setPassword] = useState('');
  const [empty, setEmpty] = useState(false);
  const passwordRef = useRef<HTMLInputElement>(null);

  const problem = stepUp.isError ? describeSignInProblem(stepUp.error) : undefined;
  const locked = problem?.kind === 'locked' || problem?.kind === 'rate_limited';

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    if (stepUp.isPending || locked) {
      return;
    }
    if (!password) {
      setEmpty(true);
      return;
    }
    setEmpty(false);
    stepUp.mutate(password, {
      onSuccess: () => {
        setPassword('');
        onDone(true);
      },
      onError: () => {
        setPassword(''); // never keep a rejected password around
      },
    });
  }

  return (
    <AppDialog
      open
      onClose={() => {
        onDone(false);
      }}
      busy={stepUp.isPending}
      maxWidth="xs"
      initialFocusRef={passwordRef}
      onSubmit={submit}
    >
      <AppDialogHeader>Confirm your password</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <DialogContentText>
            This change affects trading or credentials. Enter your password to continue; sensitive changes then stay
            unlocked for 5 minutes.
          </DialogContentText>
          {problem && <StepUpProblem problem={problem} onElapsed={stepUp.reset} />}
          <TextField
            label="Password"
            name="password"
            type="password"
            autoComplete="current-password"
            inputRef={passwordRef}
            fullWidth
            value={password}
            onChange={(event) => {
              setPassword(event.target.value);
            }}
            error={empty}
            helperText={empty ? 'Enter your password.' : undefined}
            slotProps={{ htmlInput: { 'aria-required': true } }}
          />
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button
          onClick={() => {
            onDone(false);
          }}
          disabled={stepUp.isPending}
        >
          Cancel
        </Button>
        <Button type="submit" variant="contained" loading={stepUp.isPending} disabled={locked}>
          Confirm
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}

function StepUpProblem({
  problem,
  onElapsed,
}: {
  problem: ReturnType<typeof describeSignInProblem>;
  onElapsed: () => void;
}) {
  switch (problem.kind) {
    case 'invalid_credentials':
      return <Alert severity="error">The password is incorrect.</Alert>;
    case 'locked':
    case 'rate_limited':
      return (
        <Alert severity="warning">
          Too many failed attempts. Try again in{' '}
          <RetryCountdown seconds={problem.retryAfterSeconds} onElapsed={onElapsed} />.
        </Alert>
      );
    case 'unavailable':
      return <Alert severity="error">The password could not be checked right now. Please try again.</Alert>;
  }
}
