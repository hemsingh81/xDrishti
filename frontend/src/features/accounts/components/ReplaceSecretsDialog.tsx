import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useState, type SyntheticEvent } from 'react';
import type { AccountDetail } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { useUpdateSecrets } from '../api/accountMutations';
import { dhanApp } from '../lib/dhanApp';
import { fieldErrorsOf, problemMessage, type FieldErrors } from '../lib/fieldErrors';
import { focusFirstInvalid } from '../lib/focus';
import { istDayIso } from '@/shared/lib/format';
import {
  effectiveIssuedOn,
  secretName,
  toUpdateSecretsInput,
  validateReplaceSecrets,
  type ReplaceSecretsDraft,
} from '../lib/secrets';
import { FormAlert } from './FormAlert';
import { SecretField } from './SecretField';

interface ReplaceSecretsDialogProps {
  account: AccountDetail;
  onClose: () => void;
}

const blankDraft = (): ReplaceSecretsDraft => ({
  apiKey: '',
  apiSecret: '',
  apiKeyIssuedOn: '',
  issuedOnTouched: false,
});

/**
 * Write-only replacement of the broker API key and/or secret (password confirmed on save). The typed values live only
 * in this dialog's state: rendered while open, so closing it (cancel, ×, success) discards them.
 */
export function ReplaceSecretsDialog({ account, onClose }: ReplaceSecretsDialogProps) {
  const { notify } = useToast();
  const save = useUpdateSecrets();
  const [draft, setDraft] = useState<ReplaceSecretsDraft>(blankDraft);
  const [clientErrors, setClientErrors] = useState<FieldErrors>({});
  const [bodyRef, setBodyRef] = useState<HTMLElement | null>(null);
  const errors = { ...fieldErrorsOf(save.error), ...clientErrors };

  function change(patch: Partial<ReplaceSecretsDraft>) {
    setDraft((previous) => ({ ...previous, ...patch }));
    setClientErrors({});
    save.reset();
  }

  function close() {
    setDraft(blankDraft());
    onClose();
  }

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    const found = validateReplaceSecrets(draft, account.apiKeyIssuedOn);
    setClientErrors(found);
    if (Object.keys(found).length > 0) {
      focusFirstInvalid(bodyRef);
      return;
    }
    save.mutate(
      { id: account.id, body: toUpdateSecretsInput(draft, account.apiKeyIssuedOn) },
      {
        onSuccess: () => {
          notify('Broker secrets replaced.');
          close();
        },
        onError: () => {
          focusFirstInvalid(bodyRef);
        },
      },
    );
  }

  return (
    <AppDialog open onClose={close} busy={save.isPending} onSubmit={submit}>
      <AppDialogHeader subtitle={`${account.name} · ${dhanApp.name}`}>Replace broker secrets</AppDialogHeader>
      <AppDialogBody ref={setBodyRef}>
        <Stack spacing={2}>
          <Typography color="text.secondary">
            Enter only what changed; a blank field keeps the stored value. Values are stored encrypted and never shown
            again.
          </Typography>
          <SecretField
            id="replace-apiKey"
            label={`New ${dhanApp.apiKeyLabel}`}
            value={draft.apiKey}
            error={errors.apiKey}
            helperText={`Leave blank to keep the current ${secretName('apiKey')}.`}
            onChange={(value) => {
              change({ apiKey: value });
            }}
          />
          <SecretField
            id="replace-apiSecret"
            label={`New ${dhanApp.apiSecretLabel}`}
            value={draft.apiSecret}
            error={errors.apiSecret}
            helperText={`Leave blank to keep the current ${secretName('apiSecret')}.`}
            onChange={(value) => {
              change({ apiSecret: value });
            }}
          />
          <TextField
            type="date"
            label="API key generated on"
            id="replace-issued-on"
            value={effectiveIssuedOn(draft, account.apiKeyIssuedOn)}
            onChange={(e) => {
              change({ apiKeyIssuedOn: e.target.value, issuedOnTouched: true });
            }}
            error={Boolean(errors.apiKeyIssuedOn)}
            helperText={
              errors.apiKeyIssuedOn ??
              `${dhanApp.name} API keys are valid for ${dhanApp.apiKeyValidityMonths} months from this day.`
            }
            slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: istDayIso() } }}
            sx={{ maxWidth: 260 }}
          />
          <Alert role="status" severity="info">
            You will be asked for your password. Reconnect the account afterwards so the new key is used.
          </Alert>
          <FormAlert message={problemMessage(save.error)} />
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={close} disabled={save.isPending}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" loading={save.isPending}>
          Replace secrets
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
