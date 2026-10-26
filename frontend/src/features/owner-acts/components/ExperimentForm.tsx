import { Alert, Button, Checkbox, FormControlLabel, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link as RouterLink } from 'react-router';
import { ErrorState, LoadingState } from '@/shared/ui';
import { previewQuery, useStartExperiment, type Job } from '../api/experimentApi';
import { startProblem } from './problemText';
import { PreviewFacts } from './PreviewFacts';

const maxReason = 500;
const hex64 = /^[0-9a-f]{64}$/;

/** The form for one baseline: the facts the server gives first, then the one knob, the reason and the password. */
export function ExperimentForm({ runKey, onStarted }: { runKey: string; onStarted: (job: Job) => void }) {
  const query = useQuery({ ...previewQuery(runKey), enabled: hex64.test(runKey) });
  const start = useStartExperiment();
  const [knobIndex, setKnobIndex] = useState('');
  const [value, setValue] = useState('');
  const [reason, setReason] = useState('');
  const [password, setPassword] = useState('');
  const [understood, setUnderstood] = useState(false);
  const [problem, setProblem] = useState<ReturnType<typeof startProblem> | null>(null);

  if (!hex64.test(runKey))
    return <Typography color="text.secondary">Pick a stored run, or paste its 64-character key.</Typography>;
  if (query.isPending) return <LoadingState label="Loading the preview…" />;
  if (query.isError) return <ErrorState error={query.error} />;
  const preview = query.data;
  if (!preview.rebuildable) {
    return (
      <Alert severity="info">
        This run cannot be replayed: {preview.notRebuildableReason ?? 'it cannot be rebuilt from its stored record.'}
      </Alert>
    );
  }

  const knob = preview.knobs[Number(knobIndex)];
  const needsBox = preview.bestPossibleOutcome === 'Inconclusive';
  const candidate = Number(value);
  const ready =
    knob !== undefined &&
    value.trim() !== '' &&
    Number.isFinite(candidate) &&
    reason.trim().length > 0 &&
    reason.length <= maxReason &&
    password.length > 0 &&
    (!needsBox || understood) &&
    !start.isPending;

  const submit = () => {
    if (!knob) return;
    setProblem(null);
    start.mutate(
      {
        baselineRunKey: runKey,
        kind: knob.kind === 'slippage' ? 'slippage' : 'exit',
        name: knob.name,
        candidateValue: candidate,
        reason,
        password,
      },
      {
        onError: (error) => {
          setProblem(startProblem(error, preview.activeJobId));
        },
        onSettled: () => {
          setPassword('');
          start.reset(); // the password sits in the variables of the mutation: it is dropped as soon as the call ends
        },
        onSuccess: (job) => {
          setUnderstood(false);
          onStarted(job);
        },
      },
    );
  };

  return (
    <Stack
      spacing={2}
      component="form"
      aria-label="Start a replay"
      onSubmit={(event) => {
        event.preventDefault();
        if (ready) submit();
      }}
    >
      <PreviewFacts preview={preview} />
      <TextField
        select
        label="Knob to change"
        value={knobIndex}
        onChange={(event) => {
          setKnobIndex(event.target.value);
        }}
        required
        helperText={knob?.note ?? 'Exactly one thing changes; everything else stays as in the baseline.'}
      >
        {preview.knobs.map((k, i) => (
          <MenuItem key={`${k.kind}:${k.name}`} value={String(i)}>
            {`${k.kind}: ${k.name} (now ${k.current}, allowed ${k.min} to ${k.max})`}
          </MenuItem>
        ))}
      </TextField>
      <TextField
        label="New value"
        type="number"
        value={value}
        onChange={(event) => {
          setValue(event.target.value);
        }}
        required
        disabled={!knob}
        slotProps={{ htmlInput: { step: 'any', ...(knob ? { min: knob.min, max: knob.max } : {}) } }}
        helperText={knob ? `Allowed ${knob.min} to ${knob.max}; now ${knob.current}.` : 'Pick the knob first.'}
      />
      <TextField
        label="Reason for this replay"
        value={reason}
        onChange={(event) => {
          setReason(event.target.value);
        }}
        required
        error={reason.length > maxReason}
        helperText={`${reason.length} of ${maxReason} characters`}
      />
      {needsBox && (
        <FormControlLabel
          control={
            <Checkbox
              checked={understood}
              onChange={(event) => {
                setUnderstood(event.target.checked);
              }}
            />
          }
          label="I understand a run counts as a look and can at best be Inconclusive"
        />
      )}
      <TextField
        label="Your password for this replay"
        type="password"
        autoComplete="current-password"
        value={password}
        onChange={(event) => {
          setPassword(event.target.value);
        }}
        required
        helperText="Asked again for every start. A wrong one counts toward the sign-in lock."
      />
      {problem && (
        <Alert severity="error" role="alert">
          {problem.text}{' '}
          {problem.activeJobId && <RouterLink to={`?job=${problem.activeJobId}`}>Follow the active job</RouterLink>}
        </Alert>
      )}
      <div>
        <Button type="submit" variant="contained" disabled={!ready}>
          Start replay
        </Button>
      </div>
    </Stack>
  );
}
