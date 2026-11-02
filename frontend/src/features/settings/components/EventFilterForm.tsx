import { Alert, Button, Checkbox, FormControlLabel, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { useState } from 'react';
import { ApiError } from '@/shared/api';
import type { components } from '@/shared/api/schema';
import { useChangeEventFilter } from '../api/eventFilterApi';

type View = components['schemas']['EventFilterView'];

const maxReason = 300;

/** The words of a refused change. The server's title is shown as sent; a wrong password is an answer, not a sign-out. */
function refusal(error: unknown): { text: string; settings: boolean } {
  if (!(error instanceof ApiError))
    return { text: 'Could not reach the server. Nothing was changed.', settings: false };
  const title = error.problem?.title ?? 'The request failed.';
  switch (error.status) {
    case 401:
      return { text: 'The password was not accepted. Nothing was changed.', settings: false };
    case 423:
      return { text: `Too many wrong passwords; sign-in is locked for now. ${title}`, settings: false };
    case 409:
      return {
        text: `${title} The filter was read again; check it and try again. Nothing was changed.`,
        settings: false,
      };
    default:
      return { text: `${title} Nothing was changed.`, settings: error.problem?.code === 'selection.invalid_setting' };
  }
}

/** The owner's change: on or off, the window N (0 to 10 sessions), manual or auto, a reason (1 to 300) and the password, asked every time. */
export function EventFilterForm({ view }: { view: View }) {
  const change = useChangeEventFilter();
  const [enabled, setEnabled] = useState(view.enabled);
  const [days, setDays] = useState(String(view.resultsWithinDays));
  const [approval, setApproval] = useState<'manual' | 'auto'>(view.approval === 'auto' ? 'auto' : 'manual');
  const [reason, setReason] = useState('');
  const [password, setPassword] = useState('');
  const n = Number(days);
  const nValid = days.trim() !== '' && Number.isInteger(n) && n >= 0 && n <= 10;
  const ready =
    nValid && reason.trim().length > 0 && reason.length <= maxReason && password.length > 0 && !change.isPending;
  const [problem, setProblem] = useState<{ text: string; settings: boolean } | null>(null);
  const [done, setDone] = useState(false);

  const submit = () => {
    setProblem(null);
    setDone(false);
    change.mutate(
      { enabled, resultsWithinDays: n, approval, reason, password },
      {
        onSuccess: () => {
          setReason('');
          setDone(true);
        },
        onError: (error) => {
          setProblem(refusal(error));
        },
        // The password is gone from the field and from the mutation cache (its variables) once the request is settled.
        onSettled: () => {
          setPassword('');
          change.reset();
        },
      },
    );
  };

  return (
    <Stack
      component="form"
      spacing={2}
      aria-label="Change the event filter"
      onSubmit={(event) => {
        event.preventDefault();
        if (ready) submit();
      }}
    >
      <FormControlLabel
        control={
          <Checkbox
            checked={enabled}
            onChange={(event) => {
              setEnabled(event.target.checked);
            }}
          />
        }
        label="Event filter on"
      />
      <TextField
        label="Window N (sessions, 0 to 10)"
        value={days}
        onChange={(event) => {
          setDays(event.target.value);
        }}
        error={!nValid || problem?.settings === true}
        helperText={
          !nValid ? 'A whole number from 0 to 10.' : problem?.settings ? 'The server refused this setting.' : ' '
        }
        slotProps={{ htmlInput: { inputMode: 'numeric' } }}
      />
      <TextField
        select
        label="Approval"
        value={approval}
        onChange={(event) => {
          setApproval(event.target.value === 'auto' ? 'auto' : 'manual');
        }}
        error={problem?.settings === true}
        helperText="Manual: only you switch it. Auto: it may turn on by itself, only from a hold-out confirmed KEEP, recorded as automatic."
      >
        <MenuItem value="manual">Manual</MenuItem>
        <MenuItem value="auto">Auto</MenuItem>
      </TextField>
      <TextField
        label="Reason"
        value={reason}
        onChange={(event) => {
          setReason(event.target.value);
        }}
        required
        error={reason.length > maxReason}
        helperText={`${String(reason.length)} of ${String(maxReason)} characters`}
      />
      <TextField
        label="Your password for this change"
        type="password"
        autoComplete="current-password"
        value={password}
        onChange={(event) => {
          setPassword(event.target.value);
        }}
        required
        helperText="Asked again for every change. A wrong one counts toward the sign-in lock."
      />
      <Typography variant="body2">
        This changes what the plan builder does with instruments about to report. It places no order.
      </Typography>
      {problem && <Alert severity="error">{problem.text}</Alert>}
      {done && <Alert severity="success">The event filter was changed; the new row is in the history.</Alert>}
      <div>
        <Button type="submit" variant="contained" disabled={!ready}>
          Change the event filter
        </Button>
      </div>
    </Stack>
  );
}
