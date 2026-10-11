import { Divider, Grid, Stack, TextField, Typography } from '@mui/material';
import { dhanApp } from '../../lib/dhanApp';
import type { FieldErrors } from '../../lib/fieldErrors';
import { todayIso } from '../../lib/portfolioForm';
import type { BrokerAppDraft } from '../../lib/wizardState';
import { DhanSetupGuide } from '../DhanSetupGuide';
import { SecretField } from '../SecretField';

interface BrokerAppStepProps {
  app: BrokerAppDraft;
  errors: FieldErrors;
  onChange: (patch: Partial<BrokerAppDraft>) => void;
}

/** Step 1: how to create the Dhan app, then its client id and the write-only API key and secret. */
export function BrokerAppStep({ app, errors, onChange }: BrokerAppStepProps) {
  return (
    <Stack spacing={3}>
      <DhanSetupGuide />
      <Divider />
      <Stack spacing={2}>
        <Typography variant="h3">Your {dhanApp.name} details</Typography>
        <Typography color="text.secondary">
          The API key and secret are stored encrypted and are never shown again — not even to you.
        </Typography>
        <Grid container spacing={2}>
          <Grid size={{ xs: 12, sm: 6 }}>
            <TextField
              fullWidth
              required
              label={dhanApp.clientIdLabel}
              id="wizard-client-id"
              value={app.clientId}
              autoComplete="off"
              onChange={(e) => {
                onChange({ clientId: e.target.value });
              }}
              error={Boolean(errors.clientId)}
              helperText={errors.clientId ?? dhanApp.clientIdHint}
              slotProps={{ htmlInput: { maxLength: 32, spellCheck: false, autoCapitalize: 'none' } }}
            />
          </Grid>
          <Grid size={{ xs: 12, sm: 6 }}>
            <TextField
              fullWidth
              type="date"
              label="API key generated on"
              id="wizard-issued-on"
              value={app.apiKeyIssuedOn}
              onChange={(e) => {
                onChange({ apiKeyIssuedOn: e.target.value });
              }}
              error={Boolean(errors.apiKeyIssuedOn)}
              helperText={
                errors.apiKeyIssuedOn ??
                `Valid for ${dhanApp.apiKeyValidityMonths} months; you are warned before it expires.`
              }
              slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: todayIso() } }}
            />
          </Grid>
          <Grid size={{ xs: 12, sm: 6 }}>
            <SecretField
              required
              id="wizard-apiKey"
              label={dhanApp.apiKeyLabel}
              value={app.apiKey}
              error={errors['secrets.apiKey']}
              onChange={(value) => {
                onChange({ apiKey: value });
              }}
            />
          </Grid>
          <Grid size={{ xs: 12, sm: 6 }}>
            <SecretField
              required
              id="wizard-apiSecret"
              label={dhanApp.apiSecretLabel}
              value={app.apiSecret}
              error={errors['secrets.apiSecret']}
              onChange={(value) => {
                onChange({ apiSecret: value });
              }}
            />
          </Grid>
        </Grid>
        {errors.secrets && (
          <Typography role="alert" color="error">
            {errors.secrets}
          </Typography>
        )}
      </Stack>
    </Stack>
  );
}
