import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useRef, useState, type ReactNode } from 'react';
import { moveProblem } from './moveProblem';

const maxReason = 500;

interface ActFormProps<T> {
  /** The accessible name of the form. */
  label: string;
  /** What the act does and does not do, in a sentence. */
  consequence: string;
  submitLabel: string;
  /** False while a field of the act itself is not ready. */
  fieldsReady?: boolean;
  /** Runs the act with the reason and the password typed for it. */
  run: (reason: string, password: string) => Promise<T>;
  /** The sentence after a success. */
  done: (outcome: T) => string;
  /** Called with the server's problem (after the password is cleared). */
  onRefused?: (error: unknown) => void;
  /** The fields of the act itself, drawn first. */
  children?: ReactNode;
}

/**
 * The part every owner act has: a reason (1 to 500), the password typed again for THIS act (cleared after every try, right or wrong; a wrong one is an
 * answer, not a sign-out), the consequence sentence and the server's answer. The password lives in this field only.
 */
export function ActForm<T>({
  label,
  consequence,
  submitLabel,
  fieldsReady = true,
  run,
  done,
  onRefused,
  children,
}: ActFormProps<T>) {
  const [reason, setReason] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [result, setResult] = useState<string | null>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const ready = fieldsReady && reason.trim().length > 0 && reason.length <= maxReason && password.length > 0 && !busy;

  const submit = async () => {
    setBusy(true);
    setProblem(null);
    setResult(null);
    try {
      const outcome = await run(reason, password);
      setReason('');
      setResult(done(outcome));
    } catch (error) {
      setProblem(moveProblem(error));
      onRefused?.(error);
      passwordRef.current?.focus();
    } finally {
      setPassword('');
      setBusy(false);
    }
  };

  return (
    <Stack
      component="form"
      spacing={2}
      aria-label={label}
      onSubmit={(event) => {
        event.preventDefault();
        if (ready) void submit();
      }}
    >
      {children}
      <TextField
        label="Reason"
        value={reason}
        onChange={(event) => {
          setReason(event.target.value);
        }}
        required
        error={reason.length > maxReason}
        helperText={`${reason.length} of ${maxReason} characters`}
      />
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
        helperText="Asked again for every act. A wrong one counts toward the sign-in lock."
      />
      <Typography variant="body2">{consequence}</Typography>
      {problem && <Alert severity="error">{problem}</Alert>}
      {result && <Alert severity="success">{result}</Alert>}
      <div>
        <Button type="submit" variant="contained" disabled={!ready}>
          {submitLabel}
        </Button>
      </div>
    </Stack>
  );
}
