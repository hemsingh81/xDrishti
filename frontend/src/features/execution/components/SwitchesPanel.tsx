import { Alert, Typography } from '@mui/material';

/** The master switch as the server reports it (null: could not be read, counts as OFF), with its risk direction. */
export function SwitchesPanel({ executionEnabled }: { executionEnabled: boolean | null }) {
  return (
    <section aria-label="Switches">
      <Typography variant="h2" sx={{ fontSize: 18, mb: 1 }}>
        Switches
      </Typography>
      <Typography>
        execution_enabled:{' '}
        {executionEnabled === null ? 'could not be read (counts as off)' : executionEnabled ? 'on' : 'off'}
      </Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary' }}>
        Raising risk waits one closed session and needs your password; lowering is immediate.
      </Typography>
      <Alert severity="info" sx={{ mt: 1 }}>
        Switches are changed in Settings; this build has no switch or drill endpoint on this page, so none is offered
        here.
      </Alert>
    </section>
  );
}
