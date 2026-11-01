import { Box, Button, Typography } from '@mui/material';
import { useState } from 'react';
import { formatAgo, formatIstDateTime } from '@/shared/lib/format';
import { drillAnswer } from '../lib/drillWords';
import { useRequestDrill, type DrillsView } from '../api/switchApi';
import { PasswordReasonDialog } from './PasswordReasonDialog';

/** The kill-switch drill of one account: the last result and its age, a request through the password dialog, the answers. */
export function DrillCard({ accountId, name, drills }: { accountId: string; name: string; drills: DrillsView }) {
  const request = useRequestDrill();
  const [target, setTarget] = useState<string | null>(null);
  const close = () => {
    request.reset();
    setTarget(null);
  };
  const last = drills.drills.find((d) => d.accountId === accountId);
  const now = new Date(drills.serverNow);
  const age = last ? (now.getTime() - new Date(last.at).getTime()) / 86_400_000 : 0;
  const requests = drills.requests.filter((r) => r.accountId === accountId).slice(0, 5);
  return (
    <section aria-label={`Kill-switch drill: ${name}`}>
      <Typography variant="h3" sx={{ fontSize: 16, mb: 0.5 }}>
        Kill-switch drill: {name}
      </Typography>
      <Typography>
        {last
          ? `Last drill ${last.passed ? 'passed' : 'failed'} ${formatAgo(last.at, now)} (${formatIstDateTime(last.at)}, halt seen in ${last.latencyMs} ms). ${
              age <= drills.maxAgeDays
                ? `It counts for the mode ladder for ${drills.maxAgeDays} days.`
                : `It is older than ${drills.maxAgeDays} days and no longer counts.`
            }`
          : 'No drill has been recorded.'}
      </Typography>
      {last && (
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          {last.detail}
        </Typography>
      )}
      {requests.length > 0 && (
        <Box component="ul" aria-label={`Drill requests: ${name}`} sx={{ m: 0, pl: 3 }}>
          {requests.map((r) => (
            <li key={r.commandId}>
              {formatIstDateTime(r.at)}: {drillAnswer(r.result, r.code)}
            </li>
          ))}
        </Box>
      )}
      <Typography variant="body2" sx={{ color: 'text.secondary', my: 1 }}>
        A drill sends nothing to the broker. It runs only when the execution service runs in the sandbox, and it needs a
        flat account and no halt in force.
      </Typography>
      <Button
        variant="outlined"
        onClick={() => {
          request.reset();
          setTarget(accountId);
        }}
      >
        Run drill
      </Button>
      <PasswordReasonDialog
        open={target !== null}
        title={`Run the kill-switch drill: ${name}`}
        confirmLabel="Run the drill"
        askReason={false}
        pending={request.isPending}
        error={request.error}
        onClose={close}
        onSubmit={({ password }) => {
          if (target !== null) request.mutate({ accountId: target, password }, { onSuccess: close });
        }}
      />
    </section>
  );
}
