import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import ErrorIcon from '@mui/icons-material/ReportProblemOutlined';
import VisibilityIcon from '@mui/icons-material/VisibilityOutlined';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOffOutlined';
import { Box, Button, IconButton, InputAdornment, Stack, Typography } from '@mui/material';
import { useRef, useState, type KeyboardEvent, type SyntheticEvent } from 'react';
import { useLogin } from '../api/authMutations';
import { describeSignInProblem } from '../lib/signInProblem';
import { LoginField } from './LoginField';
import { SignInProblemAlert } from './SignInProblemAlert';

/** Email + password form. On success the session cache changes and the page redirects — nothing to do here. */
export function LoginForm() {
  const login = useLogin();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [capsLock, setCapsLock] = useState(false);
  const [missing, setMissing] = useState<{ email?: string; password?: string }>({});
  const emailRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const formRef = useRef<HTMLFormElement>(null);

  const problem = login.isError ? describeSignInProblem(login.error) : undefined;
  // Locked or rate limited: the form stays blocked until the countdown ends (the alert then resets the attempt).
  const blocked = problem?.kind === 'locked' || problem?.kind === 'rate_limited';
  const disabled = blocked || login.isPending;
  // Fields are read-only + aria-disabled rather than `disabled`, so a keyboard user keeps their focus.
  const fieldAttributes = disabled ? { readOnly: true, 'aria-disabled': true } : {};

  /** Puts focus on a field unless the user has deliberately moved it elsewhere on the page. */
  function focusIfUnclaimed(field: HTMLInputElement | null) {
    const active = document.activeElement;
    if (!active || active === document.body || formRef.current?.contains(active)) {
      field?.focus();
    }
  }

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    if (disabled) {
      return;
    }
    const errors = {
      email: email.trim() ? undefined : 'Enter your email address.',
      password: password ? undefined : 'Enter your password.',
    };
    setMissing(errors);
    if (errors.email || errors.password) {
      (errors.email ? emailRef : passwordRef).current?.focus();
      return;
    }
    login.mutate(
      { email: email.trim(), password },
      {
        onError: (error) => {
          if (describeSignInProblem(error).kind === 'invalid_credentials') {
            passwordRef.current?.select();
          } else {
            // The submit button was disabled while pending; do not leave focus on the page body.
            focusIfUnclaimed(passwordRef.current);
          }
        },
      },
    );
  }

  function trackCapsLock(event: KeyboardEvent) {
    setCapsLock(event.getModifierState('CapsLock'));
  }

  return (
    <Box component="form" noValidate onSubmit={submit} aria-label="Sign in" ref={formRef}>
      <Stack spacing={1.75}>
        {problem && (
          <SignInProblemAlert
            // A new attempt restarts the countdown.
            key={login.submittedAt}
            problem={problem}
            onElapsed={() => {
              login.reset();
              focusIfUnclaimed(emailRef.current);
            }}
          />
        )}
        <LoginField
          label="Email"
          name="email"
          type="email"
          autoComplete="username"
          value={email}
          onChange={(event) => {
            setEmail(event.target.value);
          }}
          error={missing.email}
          inputRef={emailRef}
          inputProps={{ autoCapitalize: 'none', spellCheck: false, 'aria-required': true, ...fieldAttributes }}
        />
        <LoginField
          label="Password"
          name="password"
          type={showPassword ? 'text' : 'password'}
          autoComplete="current-password"
          value={password}
          onChange={(event) => {
            setPassword(event.target.value);
          }}
          onKeyDown={trackCapsLock}
          onKeyUp={trackCapsLock}
          onBlur={() => {
            setCapsLock(false);
          }}
          error={missing.password}
          inputRef={passwordRef}
          inputProps={{ 'aria-required': true, ...fieldAttributes }}
          endAdornment={
            <InputAdornment position="end">
              <IconButton
                edge="end"
                size="small"
                aria-label={showPassword ? 'Hide password' : 'Show password'}
                onClick={() => {
                  setShowPassword((value) => !value);
                }}
              >
                {showPassword ? <VisibilityOffIcon fontSize="small" /> : <VisibilityIcon fontSize="small" />}
              </IconButton>
            </InputAdornment>
          }
        />
        {capsLock && (
          <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center', color: 'warning.main' }} role="status">
            <ErrorIcon fontSize="small" />
            <Typography variant="body2">Caps Lock is on</Typography>
          </Stack>
        )}
        <Button
          type="submit"
          variant="contained"
          size="large"
          fullWidth
          loading={login.isPending}
          disabled={blocked}
          endIcon={<ChevronRightIcon />}
        >
          Sign in
        </Button>
      </Stack>
    </Box>
  );
}
