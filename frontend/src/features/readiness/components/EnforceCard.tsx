import { Alert, Box, Button, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Suspense, useEffect, useState } from 'react';
import { PasswordReasonDialog } from '@/features/execution';
import { ErrorState, LoadingState } from '@/shared/ui';
import { Facts } from '@/shared/ui/Facts';
import { useChangeEnforce } from '../api/enforceApi';
import { readinessSettingsQuery } from '../api/readinessQueries';
import { enforceMeaning } from '../lib/words';
import { Panel } from './Panel';

/**
 * The owner's enforce switch (the one write of this screen). OFF by default. Changing it, ON or OFF, asks for a fresh password
 * and a reason; the server refuses a wrong password (401) or a missing one (403) and its words are shown as sent.
 */
export function EnforceCard() {
  const query = useQuery(readinessSettingsQuery);
  const change = useChangeEnforce();
  // What the dialog will set, fixed when it opens (null = closed), so a refetch under it cannot flip the act.
  const [target, setTarget] = useState<boolean | null>(null);
  const [changed, setChanged] = useState<boolean | null>(null);
  const { reset } = change;
  const close = () => {
    setTarget(null);
    reset();
  };
  useEffect(() => reset, [reset]);

  return (
    <Panel title="Enforce switch">
      {query.isPending && <LoadingState label="Loading the enforce switch…" />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data && (
        <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
          <Facts
            items={[
              { label: 'Enforce', value: <strong>{query.data.enforce ? 'ON' : 'OFF'}</strong> },
              { label: 'What it means', value: enforceMeaning(query.data.enforce) },
              {
                label: 'Switch row',
                value: query.data.switchId === null ? 'None stored: it reads OFF' : `#${String(query.data.switchId)}`,
              },
              { label: 'Thresholds hash', value: query.data.settingsHash },
            ]}
          />
          <Box component="details">
            <Typography component="summary" variant="body2" sx={{ cursor: 'pointer' }}>
              Thresholds in force
            </Typography>
            <Box
              component="pre"
              sx={{ m: 0, mt: 1, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', typography: 'body2' }}
            >
              {query.data.settings}
            </Box>
          </Box>
          <Typography variant="body2" color="text.secondary">
            Changing the switch is the owner&apos;s act and needs a fresh password in both directions (OFF removes a
            refusal, so it is asked too). Nothing here places an order.
          </Typography>
          {changed !== null && (
            <Alert severity="success" role="status">
              Enforce is now {changed ? 'ON' : 'OFF'}.
            </Alert>
          )}
          <div>
            <Button
              variant="outlined"
              onClick={() => {
                setChanged(null);
                setTarget(!query.data.enforce);
              }}
            >
              {query.data.enforce ? 'Turn enforce OFF' : 'Turn enforce ON'}
            </Button>
          </div>
        </Box>
      )}
      {target !== null && (
        <Suspense fallback={null}>
          <PasswordReasonDialog
            open
            title={`Turn enforce ${target ? 'ON' : 'OFF'}`}
            confirmLabel={`Turn ${target ? 'ON' : 'OFF'}`}
            pending={change.isPending}
            error={change.error}
            intro={
              <Typography variant="body2" sx={{ mb: 1 }}>
                {target
                  ? 'With enforce ON a failed gate refuses Candidate to Paper, Paper to Active and Paused to Paper with 409 confidence.gate_failed. Your password is asked again for this change.'
                  : 'With enforce OFF a failed gate only warns and records its result. Your password is asked again for this change.'}
              </Typography>
            }
            onSubmit={({ password, reason }) => {
              change.mutate(
                { enforce: target, reason, password },
                {
                  onSuccess: () => {
                    setChanged(target);
                    close();
                  },
                },
              );
            }}
            onClose={close}
          />
        </Suspense>
      )}
    </Panel>
  );
}
