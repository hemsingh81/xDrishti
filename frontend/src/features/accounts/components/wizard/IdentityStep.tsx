import { Stack, Typography } from '@mui/material';
import type { IdentityFormState } from '../../lib/connectionForm';
import type { FieldErrors } from '../../lib/fieldErrors';
import { IdentityFields } from '../IdentityFields';

interface IdentityStepProps {
  identity: IdentityFormState;
  errors: FieldErrors;
  /** The name was suggested (and not typed). */
  suggested: boolean;
  onChange: (patch: Partial<IdentityFormState>) => void;
}

/** Step 2: how the account is called and shown in this app. */
export function IdentityStep({ identity, errors, suggested, onChange }: IdentityStepProps) {
  return (
    <Stack spacing={2}>
      <Typography color="text.secondary">
        Name the account and pick its colour. Both are only for you and can be changed later.
      </Typography>
      <IdentityFields
        idPrefix="wizard"
        state={identity}
        errors={errors}
        nameHint={suggested ? 'Suggested — change it if you like.' : undefined}
        onChange={onChange}
      />
    </Stack>
  );
}
