import { Box, Button, Grid, Stack, TextField } from '@mui/material';
import { useRef, useState, type SyntheticEvent } from 'react';
import type { AccountDetail } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { useUpdateConnection } from '../api/accountMutations';
import {
  connectionFormFrom,
  toConnectionInput,
  validateClientId,
  validateIdentity,
  type ConnectionFormState,
} from '../lib/connectionForm';
import { dhanApp } from '../lib/dhanApp';
import { fieldErrorsOf, problemMessage, type FieldErrors } from '../lib/fieldErrors';
import { focusFirstInvalid } from '../lib/focus';
import { FormAlert } from './FormAlert';
import { IdentityFields } from './IdentityFields';

/** Client id (blank keeps it — it is only ever shown masked), name, colour and notes. Password confirmed on save. */
export function ConnectionForm({ account }: { account: AccountDetail }) {
  const { notify } = useToast();
  const save = useUpdateConnection();
  const formRef = useRef<HTMLFormElement>(null);
  const [state, setState] = useState<ConnectionFormState>(() => connectionFormFrom(account));
  const [clientErrors, setClientErrors] = useState<FieldErrors>({});
  const errors = { ...fieldErrorsOf(save.error), ...clientErrors };

  function change(patch: Partial<ConnectionFormState>) {
    setState((previous) => ({ ...previous, ...patch }));
    setClientErrors({});
    save.reset();
  }

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    const found: Record<string, string> = { ...validateIdentity(state) };
    const clientIdError = validateClientId(state.clientId, false);
    if (clientIdError) {
      found.clientId = clientIdError;
    }
    setClientErrors(found);
    if (Object.keys(found).length > 0) {
      focusFirstInvalid(formRef.current);
      return;
    }
    save.mutate(
      { id: account.id, body: toConnectionInput(state) },
      {
        onSuccess: () => {
          notify('Connection details saved.');
        },
        onError: () => {
          focusFirstInvalid(formRef.current);
        },
      },
    );
  }

  return (
    <Box component="form" noValidate onSubmit={submit} ref={formRef} aria-label="Connection details">
      <Stack spacing={2.5}>
        <Grid container spacing={2}>
          <Grid size={{ xs: 12, sm: 8, md: 6 }}>
            <TextField
              fullWidth
              label={dhanApp.clientIdLabel}
              id="connection-client-id"
              value={state.clientId}
              autoComplete="off"
              onChange={(e) => {
                change({ clientId: e.target.value });
              }}
              error={Boolean(errors.clientId)}
              helperText={errors.clientId ?? `Current: ${account.maskedClientId}. Leave blank to keep it.`}
              slotProps={{ htmlInput: { maxLength: 32, spellCheck: false, autoCapitalize: 'none' } }}
            />
          </Grid>
        </Grid>
        <IdentityFields idPrefix="connection" state={state} errors={errors} onChange={change} />
        <FormAlert message={problemMessage(save.error)} />
        <Box>
          <Button type="submit" variant="contained" loading={save.isPending}>
            Save connection
          </Button>
        </Box>
      </Stack>
    </Box>
  );
}
