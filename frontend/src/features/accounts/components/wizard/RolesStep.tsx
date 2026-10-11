import { Alert, Stack, Typography } from '@mui/material';
import type { FieldErrors } from '../../lib/fieldErrors';
import type { RoleDraft } from '../../lib/roleChanges';
import type { WizardState } from '../../lib/wizardState';
import { RoleToggle } from '../RoleToggle';

interface RolesStepProps {
  state: WizardState;
  errors: FieldErrors;
  onChange: (patch: Partial<WizardState>) => void;
}

/** Step 3: what the account is used for. */
export function RolesStep({ state, errors, onChange }: RolesStepProps) {
  const { roles } = state;
  const set = (patch: Partial<RoleDraft>) => {
    onChange({ roles: { ...roles, ...patch } });
  };
  const messages = [
    errors.roles,
    errors['roles.data.kind'],
    errors['roles.data'],
    errors['roles.trading'],
    errors['roles.portfolio'],
  ].filter((m) => m !== undefined);
  return (
    <Stack spacing={1.5}>
      <Typography color="text.secondary">Choose what this account is used for. You can change this later.</Typography>
      {messages.length > 0 && (
        <Alert severity="error">
          {messages.map((message) => (
            <div key={message}>{message}</div>
          ))}
        </Alert>
      )}
      <RoleToggle
        roleKey="data"
        checked={roles.data}
        onChange={(data) => {
          set({ data });
        }}
        dataKind={{
          value: roles.dataKind,
          disabled: false,
          onChange: (dataKind) => {
            set({ dataKind });
          },
        }}
      />
      <RoleToggle
        roleKey="trading"
        checked={roles.trading}
        onChange={(trading) => {
          set({ trading });
        }}
      />
      <RoleToggle
        roleKey="portfolio"
        checked={roles.portfolio}
        onChange={(portfolio) => {
          set({ portfolio });
        }}
      />
    </Stack>
  );
}
