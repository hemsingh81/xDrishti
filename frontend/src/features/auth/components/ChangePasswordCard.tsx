import LockIcon from '@mui/icons-material/LockOutlined';
import { Alert, Box, Button, Stack, Typography } from '@mui/material';
import { visuallyHidden } from '@mui/utils';
import { useQuery } from '@tanstack/react-query';
import { useRef, useState, type SyntheticEvent } from 'react';
import { formatDate } from '@/shared/lib/format';
import { useToast } from '@/shared/ui/toastContext';
import { accountQuery, useChangePassword } from '../api/accountApi';
import { useSession } from '../api/sessionQuery';
import { describePasswordProblem, type PasswordProblem } from '../lib/passwordProblem';
import { AccountCard } from './AccountCard';
import { PasswordField } from './PasswordField';
import { SignInProblemAlert } from './SignInProblemAlert';

const minLength = 10;

interface Shown {
  current?: string;
  next?: string;
  confirm?: string;
}

/** Current password, new password twice. Other devices are signed out by the API; this session stays. */
export function ChangePasswordCard() {
  const change = useChangePassword();
  const { notify } = useToast();
  const session = useSession();
  const changedAt = useQuery(accountQuery).data?.passwordChangedAt;
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [shown, setShown] = useState<Shown>({});
  // New key = empty fields with their show/hide switches back to hidden.
  const [generation, setGeneration] = useState(0);
  const currentRef = useRef<HTMLInputElement>(null);
  const nextRef = useRef<HTMLInputElement>(null);
  const confirmRef = useRef<HTMLInputElement>(null);

  // The outcome is kept here, not in the mutation: `change.reset()` then drops the typed passwords (the mutation's
  // `variables`) as soon as the request is over.
  const [problem, setProblem] = useState<PasswordProblem>();
  const [attempt, setAttempt] = useState(0);
  const locked = problem?.kind === 'locked';
  const email = session.data?.status === 'authenticated' ? session.data.session.user.email : '';
  const mismatch = confirm !== '' && confirm !== next ? 'The passwords do not match.' : undefined;

  const currentError =
    shown.current ?? (problem?.kind === 'wrong_current' ? 'The current password is incorrect.' : undefined);
  const nextError = shown.next ?? (problem?.kind === 'new_password' ? problem.message : undefined);
  const confirmError = shown.confirm ?? mismatch;

  function edit(update: () => void) {
    update();
    setShown({});
    if (!locked) {
      setProblem(undefined);
    }
  }

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    if (locked || change.isPending) {
      return;
    }
    const errors: Shown = {
      ...(current ? {} : { current: 'Enter your current password.' }),
      ...(next.length >= minLength ? {} : { next: `Use at least ${String(minLength)} characters.` }),
      ...(confirm === next ? {} : { confirm: 'The passwords do not match.' }),
    };
    setShown(errors);
    const firstInvalid = errors.current ? currentRef : errors.next ? nextRef : errors.confirm ? confirmRef : undefined;
    if (firstInvalid) {
      firstInvalid.current?.focus();
      return;
    }
    setProblem(undefined);
    setAttempt((value) => value + 1);
    change.mutate(
      { currentPassword: current, newPassword: next },
      {
        onSuccess: () => {
          setCurrent('');
          setNext('');
          setConfirm('');
          setGeneration((value) => value + 1);
          change.reset();
          notify('Password changed — other devices were signed out');
        },
        onError: (error) => {
          const found = describePasswordProblem(error);
          const kind = found.kind;
          setProblem(found);
          change.reset();
          if (kind === 'wrong_current') {
            setCurrent(''); // never keep a rejected password around
            currentRef.current?.focus();
          } else if (kind === 'new_password') {
            nextRef.current?.focus();
          }
        },
      },
    );
  }

  return (
    <AccountCard
      id="password"
      title="Change password"
      icon={LockIcon}
      hue="rose"
      description={
        changedAt
          ? `Last changed ${formatDate(changedAt)}. Changing it signs out your other devices; this one stays signed in.`
          : 'Changing it signs out your other devices; this one stays signed in.'
      }
    >
      <Box component="form" noValidate onSubmit={submit} aria-label="Change password">
        <Stack spacing={2} sx={{ maxWidth: 480 }}>
          {/* Lets a password manager file the new password under the right login; not for people. Rendered (password
              managers skip display:none) but visually hidden, out of the tab order and hidden from assistive tech. */}
          <Box
            component="input"
            type="text"
            name="username"
            autoComplete="username"
            value={email}
            readOnly
            tabIndex={-1}
            aria-hidden
            sx={visuallyHidden}
          />
          {problem?.kind === 'locked' && (
            <SignInProblemAlert
              // A new attempt restarts the countdown.
              key={attempt}
              problem={problem.problem}
              onElapsed={() => {
                setProblem(undefined);
              }}
            />
          )}
          {problem?.kind === 'busy' && (
            <Alert severity="warning">Another change is in progress. Wait a moment and try again.</Alert>
          )}
          {problem?.kind === 'failed' && (
            <Alert severity="error">
              The password could not be changed. Check your connection and try again; nothing was changed.
            </Alert>
          )}
          <PasswordField
            key={`current-${String(generation)}`}
            label="Current password"
            name="currentPassword"
            autoComplete="current-password"
            value={current}
            onChange={(value) => {
              edit(() => {
                setCurrent(value);
              });
            }}
            error={currentError}
            locked={locked}
            inputRef={currentRef}
          />
          <PasswordField
            key={`next-${String(generation)}`}
            label="New password"
            name="newPassword"
            autoComplete="new-password"
            value={next}
            onChange={(value) => {
              edit(() => {
                setNext(value);
              });
            }}
            error={nextError}
            hint={`At least ${String(minLength)} characters`}
            locked={locked}
            inputRef={nextRef}
          />
          <PasswordField
            key={`confirm-${String(generation)}`}
            label="Confirm new password"
            name="confirmPassword"
            autoComplete="new-password"
            value={confirm}
            onChange={(value) => {
              edit(() => {
                setConfirm(value);
              });
            }}
            error={confirmError}
            locked={locked}
            inputRef={confirmRef}
          />
          <Box>
            <Button type="submit" variant="contained" loading={change.isPending} disabled={locked}>
              Change password
            </Button>
          </Box>
        </Stack>
      </Box>
      <Typography variant="body2" color="text.secondary" sx={{ mt: 2, maxWidth: '70ch' }}>
        The Keychain items used at first set-up (<code className="mono">xdrishti/auth/owner-password</code>) are not
        updated by a change here. Running <code className="mono">deploy/xd-up.sh --owner-reset</code> would overwrite
        this password from them.
      </Typography>
    </AccountCard>
  );
}
