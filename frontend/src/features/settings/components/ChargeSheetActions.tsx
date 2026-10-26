import { Alert, Button, Stack, TextField } from '@mui/material';
import { useState } from 'react';
import type { ChargeSheetStatusView } from '@/shared/api';
import { useChargeSheetAct, type ChargeSheetAct } from '../api/chargeSheetApi';
import { actProblemText } from '../lib/chargeSheetWords';

const maxReason = 500;

/**
 * The owner's act: a reason and the password, typed for this act (cleared after every try, never kept). Three buttons: allow or
 * stop allowing confirming (off until the owner switches it on), confirm the sheet shown at the rates shown, revoke the confirmation.
 * Nothing here confirms by itself, and the server refuses each act whose rules are not met.
 */
export function ChargeSheetActions({ status, shown }: { status: ChargeSheetStatusView; shown: string }) {
  const [reason, setReason] = useState('');
  const [password, setPassword] = useState('');
  const act = useChargeSheetAct(shown);
  const ready = reason.trim().length > 0 && reason.length <= maxReason && password.length > 0 && !act.isPending;
  const sheet = status.sheet;
  const canRevoke = ['confirmed', 'stale', 'sheet_missing'].includes(status.state);

  const run = (next: ChargeSheetAct) => {
    act.mutate(next, {
      onSettled: () => {
        // The field is cleared at once; the mutation state keeps the last variables in memory only until the next act (never persisted or logged).
        setPassword('');
      },
      onSuccess: () => {
        setReason('');
      },
    });
  };

  return (
    <Stack
      component="form"
      spacing={2}
      onSubmit={(event) => {
        event.preventDefault();
      }}
      aria-label="Your act"
    >
      <TextField
        label="Why (checked against what)"
        value={reason}
        onChange={(event) => {
          setReason(event.target.value);
        }}
        multiline
        minRows={2}
        required
        error={reason.length > maxReason}
        helperText={`${reason.length} of ${maxReason} characters`}
      />
      <TextField
        label="Your password"
        type="password"
        autoComplete="current-password"
        value={password}
        onChange={(event) => {
          setPassword(event.target.value);
        }}
        required
        helperText="Asked again for every act. A wrong one counts toward the sign-in lock."
      />
      {act.isError && <Alert severity="error">{actProblemText(act.error)}</Alert>}
      {act.isSuccess && (
        <Alert severity="success" role="status">
          {act.data.changed ? act.data.status.text : `Nothing changed: ${act.data.status.text}`}
        </Alert>
      )}
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
        <Button
          variant="contained"
          disabled={!ready || !sheet || !status.capabilityOn}
          onClick={() => {
            if (sheet) run({ kind: 'confirm', sheetId: sheet.id, ratesSha256: sheet.ratesSha256, reason, password });
          }}
        >
          Confirm this sheet
        </Button>
        <Button
          variant="outlined"
          color="warning"
          disabled={!ready || !canRevoke}
          onClick={() => {
            run({ kind: 'revoke', reason, password });
          }}
        >
          Revoke confirmation
        </Button>
        <Button
          variant="text"
          disabled={!ready}
          onClick={() => {
            run({ kind: 'capability', on: !status.capabilityOn, reason, password });
          }}
        >
          {status.capabilityOn ? 'Stop allowing confirming' : 'Allow confirming'}
        </Button>
      </Stack>
    </Stack>
  );
}
