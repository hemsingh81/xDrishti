import { Alert, Button, Paper, Stack, TextField, Typography } from '@mui/material';
import { useState } from 'react';
import { ApiError } from '@/shared/api';
import type { components } from '@/shared/api/schema';
import { formatIstDateTime } from '@/shared/lib/format';
import { useSwitchCapability } from '../api/capabilitiesApi';

type Item = components['schemas']['CapabilityView'];

const maxReason = 500;

function problemText(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Could not reach the server. Nothing was changed.';
  if (error.status === 401) return 'The password was not accepted. Nothing was changed.';
  return error.problem?.title ?? 'The request failed. Nothing was changed.';
}

/**
 * One switch. Switching on only allows an act: the password is typed for this switch (cleared after every try, never kept),
 * switching off needs none. Nothing here applies anything.
 */
export function CapabilityCard({ item }: { item: Item }) {
  const [reason, setReason] = useState('');
  const [password, setPassword] = useState('');
  const change = useSwitchCapability();
  const turningOn = !item.enabled;
  const ready =
    reason.trim().length > 0 && reason.length <= maxReason && (!turningOn || password.length > 0) && !change.isPending;
  const id = `capability-${item.name}`;

  const submit = () => {
    change.mutate(
      { name: item.name, enabled: turningOn, reason, ...(turningOn ? { password } : {}) },
      {
        onSettled: () => {
          setPassword('');
        },
        onSuccess: () => {
          setReason('');
        },
      },
    );
  };

  return (
    <Paper
      variant="outlined"
      component="form"
      aria-labelledby={`${id}-title`}
      sx={{ p: 2 }}
      onSubmit={(event) => {
        event.preventDefault();
        if (ready) submit();
      }}
    >
      <Stack spacing={1.5}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'baseline', flexWrap: 'wrap' }}>
          <Typography id={`${id}-title`} variant="h3" component="h2">
            {item.label}
          </Typography>
          <Typography component="span" sx={{ fontWeight: 600 }}>
            {item.enabled ? 'On' : 'Off'}
          </Typography>
        </Stack>
        <Typography>{item.meaning}</Typography>
        {item.name === 'pwa' && (
          <Typography color="text.secondary">
            Installing needs a secure address: this Mac (localhost) qualifies; a phone needs a private HTTPS path that
            you set up yourself (see the phone-access runbook). Nothing is made reachable from here, and no data is kept
            offline.
          </Typography>
        )}
        <Typography color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
          {item.changedAt
            ? `Last switched ${formatIstDateTime(item.changedAt)}: ${item.reason ?? ''}`
            : 'Never switched (off by default).'}
        </Typography>
        <TextField
          label={`Reason for ${item.label}`}
          value={reason}
          onChange={(event) => {
            setReason(event.target.value);
          }}
          required
          error={reason.length > maxReason}
          helperText={`${reason.length} of ${maxReason} characters`}
        />
        {turningOn && (
          <TextField
            label={`Your password for ${item.label}`}
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => {
              setPassword(event.target.value);
            }}
            required
            helperText="Asked again for this switch. A wrong one counts toward the sign-in lock."
          />
        )}
        {change.isError && <Alert severity="error">{problemText(change.error)}</Alert>}
        <div>
          <Button
            type="submit"
            variant={turningOn ? 'contained' : 'outlined'}
            disabled={!ready}
            aria-label={`${turningOn ? 'Switch on' : 'Switch off'} ${item.label}`}
          >
            {turningOn ? 'Switch on' : 'Switch off'}
          </Button>
        </div>
      </Stack>
    </Paper>
  );
}
