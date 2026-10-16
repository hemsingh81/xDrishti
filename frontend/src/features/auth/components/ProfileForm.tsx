import { Alert, Box, Button, Stack, TextField, Typography } from '@mui/material';
import { useRef, useState, type SyntheticEvent } from 'react';
import { ApiError } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { useUpdateProfile, type OwnerAccount } from '../api/accountApi';
import { isStepUpCancelled } from '../lib/stepUpErrors';

const maxName = 128;
const maxEmail = 256;

interface Fields {
  displayName?: string;
  email?: string;
}

function validate(displayName: string, email: string): Fields {
  const name = displayName.trim();
  const mail = email.trim();
  const errors: Fields = {};
  if (!name) {
    errors.displayName = 'Enter your name.';
  } else if (name.length > maxName) {
    errors.displayName = `Use at most ${String(maxName)} characters.`;
  }
  if (!mail.includes('@')) {
    errors.email = 'Enter a valid e-mail address.';
  } else if (mail.length > maxEmail) {
    errors.email = `Use at most ${String(maxEmail)} characters.`;
  }
  return errors;
}

/** The two fields and Save. Starts from the saved account and follows what the API stored after a save. */
export function ProfileForm({ account }: { account: OwnerAccount }) {
  const save = useUpdateProfile();
  const { notify } = useToast();
  const [displayName, setDisplayName] = useState(account.displayName);
  const [email, setEmail] = useState(account.email);
  const [shown, setShown] = useState<Fields>({});
  const [nothingToSave, setNothingToSave] = useState(false);
  const nameRef = useRef<HTMLInputElement>(null);
  const emailRef = useRef<HTMLInputElement>(null);

  const unchanged = displayName.trim() === account.displayName && email.trim().toLowerCase() === account.email;
  const serverErrors: Fields =
    save.error instanceof ApiError
      ? {
          ...(save.error.problem?.errors?.displayName
            ? { displayName: save.error.problem.errors.displayName.join(' ') }
            : {}),
          ...(save.error.problem?.errors?.email ? { email: save.error.problem.errors.email.join(' ') } : {}),
        }
      : {};
  const hasFieldErrors = Boolean(serverErrors.displayName ?? serverErrors.email);
  const failure =
    save.isError && !isStepUpCancelled(save.error) && !hasFieldErrors
      ? save.error instanceof ApiError
        ? (save.error.problem?.title ?? 'The profile could not be saved.')
        : 'Could not reach the server. Check your connection and try again.'
      : null;
  const nameError = shown.displayName ?? serverErrors.displayName;
  const emailError = shown.email ?? serverErrors.email;

  function change(update: () => void) {
    update();
    setShown({});
    setNothingToSave(false);
    save.reset();
  }

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    if (save.isPending) {
      return;
    }
    if (unchanged) {
      setNothingToSave(true);
      return;
    }
    const errors = validate(displayName, email);
    setShown(errors);
    if (errors.displayName || errors.email) {
      (errors.displayName ? nameRef : emailRef).current?.focus();
      return;
    }
    save.mutate(
      { displayName: displayName.trim(), email: email.trim() },
      {
        onSuccess: (saved) => {
          // Show what was stored (the API lower-cases the e-mail and trims both).
          setDisplayName(saved.displayName);
          setEmail(saved.email);
          setNothingToSave(false);
          notify('Profile saved');
        },
        onError: (error) => {
          if (!isStepUpCancelled(error)) {
            const first = error instanceof ApiError && error.problem?.errors?.displayName ? nameRef : emailRef;
            first.current?.focus();
          }
        },
      },
    );
  }

  return (
    <Box component="form" noValidate onSubmit={submit} aria-label="Profile">
      <Stack spacing={2} sx={{ maxWidth: 480 }}>
        {failure && <Alert severity="error">{failure}</Alert>}
        <TextField
          label="Display name"
          name="displayName"
          autoComplete="name"
          fullWidth
          value={displayName}
          onChange={(event) => {
            change(() => {
              setDisplayName(event.target.value);
            });
          }}
          error={Boolean(nameError)}
          helperText={nameError}
          inputRef={nameRef}
          slotProps={{ htmlInput: { 'aria-required': true } }}
        />
        <TextField
          label="E-mail"
          name="email"
          type="email"
          autoComplete="username"
          fullWidth
          value={email}
          onChange={(event) => {
            change(() => {
              setEmail(event.target.value);
            });
          }}
          error={Boolean(emailError)}
          helperText={emailError ?? 'You sign in with this address.'}
          inputRef={emailRef}
          slotProps={{ htmlInput: { autoCapitalize: 'none', spellCheck: false, 'aria-required': true } }}
        />
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
          <Button type="submit" variant="contained" loading={save.isPending}>
            Save profile
          </Button>
          {/* A live region that is always rendered, so the message is announced when it appears. */}
          <Typography variant="body2" color="text.secondary" role="status">
            {nothingToSave ? 'No changes to save' : ''}
          </Typography>
        </Stack>
      </Stack>
    </Box>
  );
}
