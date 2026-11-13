import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useMutation } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { holdOutReplayProblem } from '../../api/holdOutReplayApi';

interface Props {
  title: string;
  warning: string;
  confirmLabel: string;
  act: (password: string) => Promise<unknown>;
  onDone: () => void;
  onClose: () => void;
}

/** An owner act that asks the password again (SR-03): cleared after every try, never kept, and the refusal said in words. */
export function PasswordActDialog({ title, warning, confirmLabel, act, onDone, onClose }: Props) {
  const [password, setPassword] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const ref = useRef<HTMLInputElement>(null);
  const mutation = useMutation({
    gcTime: 0,
    mutationFn: act,
    onSuccess: onDone,
    onError: (error) => {
      setProblem(holdOutReplayProblem(error));
    },
    onSettled: () => {
      setPassword('');
    },
  });
  const ready = password.length > 0 && !mutation.isPending;
  return (
    <AppDialog
      open
      onClose={onClose}
      busy={mutation.isPending}
      initialFocusRef={ref}
      maxWidth="xs"
      onSubmit={(event) => {
        event.preventDefault();
        if (!ready) return;
        setProblem(null);
        mutation.mutate(password);
      }}
    >
      <AppDialogHeader>{title}</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <Typography variant="body2">{warning}</Typography>
          <TextField
            inputRef={ref}
            label="Your password for this act"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => {
              setPassword(event.target.value);
            }}
            required
            helperText="Asked again for every act. A wrong one counts toward the sign-in lock."
          />
          {problem !== null && <Alert severity="error">{problem}</Alert>}
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={mutation.isPending}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" disabled={!ready}>
          {confirmLabel}
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
