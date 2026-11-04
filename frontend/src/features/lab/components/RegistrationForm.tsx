import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useToast } from '@/shared/ui/toastContext';
import { ApiError } from '@/shared/api';
import {
  confidenceProblem,
  maxPartLength,
  registerVersion,
  registrationKey,
  type RegistrationText,
} from '../api/confidenceQueries';
import { runKeys } from '../api/runQueries';
import { registrationParts } from './registrationParts';

const empty: RegistrationText = {
  counterparty: '',
  parameterSigns: '',
  applicability: '',
  expectedEdge: '',
  falsifier: '',
};

/** The five fields, each up to 2,000 characters with a counter. Plain text only; the server validates it again. */
export function RegistrationForm({ strategyId, version }: { strategyId: string; version: number }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [values, setValues] = useState(empty);
  const [problem, setProblem] = useState<string | null>(null);
  // shortcut: no plan-freeze flag on GET registration; the form learns of a freeze from the 409
  const [frozen, setFrozen] = useState(false);
  const register = useMutation({
    mutationFn: () => registerVersion(strategyId, version, values),
    onSuccess: () => {
      toast.notify(`Registered ${strategyId} version ${version}.`, 'success');
    },
    onError: (error) => {
      if (error instanceof ApiError && error.problem?.code === 'lab.registration_frozen') setFrozen(true);
      setProblem(confidenceProblem(error, 'Nothing was stored.'));
    },
    // Whatever the answer, read the registration again: a 409 means it exists or is frozen now.
    // A stored registration also changes the report of every run of the version.
    onSettled: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: registrationKey(strategyId, version) }),
        queryClient.invalidateQueries({ queryKey: runKeys.reports }),
      ]),
  });
  const parts = registrationParts.map((p) => ({ ...p, length: values[p.key].length }));
  const ready =
    parts.every((p) => values[p.key].trim() !== '' && p.length <= maxPartLength) && !register.isPending && !frozen;

  return (
    <Stack
      component="form"
      spacing={2}
      aria-label="Register this version"
      onSubmit={(event) => {
        event.preventDefault();
        if (!ready) return;
        setProblem(null);
        register.mutate();
      }}
    >
      <Typography variant="body2" color="text.secondary">
        Plain text only. It is written once and cannot be changed afterwards, and not after a full test has fixed the
        plan of this version.
      </Typography>
      {parts.map((p) => (
        <TextField
          key={p.key}
          label={p.label}
          value={values[p.key]}
          onChange={(event) => {
            setValues({ ...values, [p.key]: event.target.value });
          }}
          required
          disabled={frozen}
          multiline
          minRows={2}
          error={p.length > maxPartLength}
          helperText={`${p.hint} ${p.length} of ${maxPartLength} characters`}
        />
      ))}
      {problem && <Alert severity="error">{problem}</Alert>}
      <div>
        <Button type="submit" variant="contained" disabled={!ready}>
          Register
        </Button>
      </div>
    </Stack>
  );
}
