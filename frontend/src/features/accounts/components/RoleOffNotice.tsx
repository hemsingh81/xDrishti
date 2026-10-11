import { Alert, Button } from '@mui/material';
import { roleInfo, type RoleKey } from '../lib/labels';

/** Shown on a settings tab while the role is off: settings can still be prepared, they just are not used. */
export function RoleOffNotice({ roleKey, onEnable }: { roleKey: RoleKey; onEnable: () => void }) {
  return (
    <Alert
      role="status"
      severity="info"
      action={
        <Button color="inherit" size="small" onClick={onEnable}>
          Enable in Roles
        </Button>
      }
    >
      The <strong>{roleInfo[roleKey].label}</strong> role is off for this account, so these settings are saved but not
      used. Complete them here, then switch the role on.
    </Alert>
  );
}
