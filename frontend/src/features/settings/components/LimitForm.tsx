import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type ReactNode } from 'react';
import { ApiError } from '@/shared/api';
import { limitsKeys } from '../api/limitsApi';
import { limitRefusal } from '../lib/limitRefusal';

interface Props {
  /** The accessible name of the form. */
  label: string;
  submitLabel: string;
  /** What the change does, in a sentence. */
  consequence: string;
  /** False while nothing differs from the stored values or a field is not valid. */
  fieldsReady: boolean;
  /** Runs the change with the password typed for it; true when the stored row says it loosened a limit (the server's direction, never a client rule). */
  run: (password: string) => Promise<boolean>;
  children: ReactNode;
}

/**
 * What the three limit forms share (BK-10b, the P8-19 pattern): the fields of the form, the password typed again for THIS change (cleared after every try, right or wrong), the server's refusal in words,
 * and after a change the sentence that says whether the server recorded it as a loosening. A change reloads every limit read.
 */
export function LimitForm({ label, submitLabel, consequence, fieldsReady, run, children }: Props) {
  const queryClient = useQueryClient();
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [done, setDone] = useState<{ loosened: boolean } | null>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const ready = fieldsReady && password.length > 0 && !busy;

  const submit = async () => {
    setBusy(true);
    setProblem(null);
    setDone(null);
    try {
      const loosened = await run(password);
      setDone({ loosened });
    } catch (error) {
      setProblem(limitRefusal(error));
      if (error instanceof ApiError && error.status === 409)
        void queryClient.invalidateQueries({ queryKey: limitsKeys.all });
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
        label="Your password for this change"
        type="password"
        autoComplete="current-password"
        value={password}
        inputRef={passwordRef}
        onChange={(event) => {
          setPassword(event.target.value);
        }}
        required
        helperText="Asked again for every change. A wrong one counts toward the sign-in lock."
      />
      <Typography variant="body2">{consequence}</Typography>
      {problem && <Alert severity="error">{problem}</Alert>}
      {done && (
        <Alert severity={done.loosened ? 'warning' : 'success'}>
          {done.loosened
            ? 'This loosens a limit: it will be shown on the next plan until dismissed. The new row is in the history.'
            : 'Changed; the new row is in the history.'}
        </Alert>
      )}
      <div>
        <Button type="submit" variant="contained" disabled={!ready}>
          {submitLabel}
        </Button>
      </div>
    </Stack>
  );
}
