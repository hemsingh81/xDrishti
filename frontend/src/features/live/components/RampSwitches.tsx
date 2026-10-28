import { Button, Stack, Typography } from '@mui/material';
import type { LiveSettingsView } from '../api/rampQueries';

const word = (on: boolean) => (on ? 'ON' : 'OFF');

/** The two live switches as words (read only); the button only asks the tab to open the dialog. */
export function RampSwitches({ settings, onChange }: { settings: LiveSettingsView; onChange: () => void }) {
  return (
    <section aria-label="Live switches">
      <Typography variant="h3" component="h2">
        Live switches
      </Typography>
      <Stack sx={{ gap: 0.5 }}>
        <Typography>Approve live tickets by rule: {word(settings.autoApproveTickets)}</Typography>
        <Typography>Raise S1 to S2 by rule: {word(settings.autoRampUp)}</Typography>
        <div>
          <Button variant="outlined" disabled={!settings.writesEnabled} onClick={onChange}>
            Change the switches
          </Button>
        </div>
        {!settings.writesEnabled && (
          <Typography variant="body2" color="text.secondary">
            Writing is switched off in this deployment (<code>Live__Writes__Enabled</code> is false).
          </Typography>
        )}
      </Stack>
    </section>
  );
}
