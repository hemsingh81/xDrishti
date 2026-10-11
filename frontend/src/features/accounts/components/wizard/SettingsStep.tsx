import { Alert, Box, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { ErrorState, LoadingState } from '@/shared/ui';
import { bucketsQuery, riskProfilesQuery } from '../../api/accountsQueries';
import type { FieldErrors } from '../../lib/fieldErrors';
import { scoped } from '../../lib/fieldErrors';
import { autoBucketNameOf, maxLiveFeedBudget } from '../../lib/portfolioForm';
import type { WizardState } from '../../lib/wizardState';
import { BucketAdder } from '../BucketAdder';
import { FirstImportSection } from '../firstImport/FirstImportSection';
import { PortfolioFields } from '../PortfolioFields';
import { TradingFields } from '../TradingFields';

interface SettingsStepProps {
  state: WizardState;
  errors: FieldErrors;
  onChange: (patch: Partial<WizardState>) => void;
}

/** Step 4: settings for the chosen roles only (same fields as the account tabs). */
export function SettingsStep({ state, errors, onChange }: SettingsStepProps) {
  const profiles = useQuery({ ...riskProfilesQuery, enabled: state.roles.trading });
  const buckets = useQuery({ ...bucketsQuery, enabled: state.roles.portfolio });

  if ((state.roles.trading && profiles.isPending) || (state.roles.portfolio && buckets.isPending)) {
    return <LoadingState label="Loading settings" />;
  }
  if (profiles.error ?? buckets.error) {
    return <ErrorState error={profiles.error ?? buckets.error} />;
  }
  return (
    <Stack spacing={3}>
      {state.roles.trading && profiles.data && (
        <Section title="Trading">
          <TradingFields
            idPrefix="wizard-trading"
            state={state.trading}
            errors={scoped(errors, 'trading')}
            profiles={profiles.data}
            onChange={(patch) => {
              onChange({ trading: { ...state.trading, ...patch } });
            }}
          />
        </Section>
      )}
      {state.roles.portfolio && buckets.data && (
        <Section title="Portfolio">
          <PortfolioFields
            idPrefix="wizard-portfolio"
            state={state.portfolio}
            errors={scoped(errors, 'portfolio')}
            buckets={buckets.data}
            ownBucketName={autoBucketNameOf(state.identity.name)}
            ownBucketPending
            onChange={(patch) => {
              onChange({ portfolio: { ...state.portfolio, ...patch } });
            }}
          />
          <BucketAdder />
          <FirstImportSection
            idPrefix="wizard-portfolio"
            date={state.portfolio.firstImportDate}
            dateError={scoped(errors, 'portfolio').firstImportDate}
            onDateChange={(firstImportDate) => {
              onChange({ portfolio: { ...state.portfolio, firstImportDate } });
            }}
            hasHoldings={false}
          />
        </Section>
      )}
      {state.roles.data && (
        <Section title="Data">
          <TextField
            label="Live-feed budget (instruments)"
            type="number"
            value={state.liveFeedBudget}
            onChange={(e) => {
              onChange({ liveFeedBudget: e.target.value });
            }}
            error={Boolean(errors['data.liveFeedBudget'])}
            helperText={errors['data.liveFeedBudget'] ?? `Up to ${maxLiveFeedBudget.toLocaleString('en-IN')}.`}
            sx={{ maxWidth: 360 }}
            slotProps={{ htmlInput: { min: 0, max: maxLiveFeedBudget, step: 1, inputMode: 'numeric' } }}
          />
        </Section>
      )}
      {!state.roles.trading && !state.roles.portfolio && !state.roles.data && (
        <Alert role="status" severity="info">
          No roles chosen — go back and choose at least one.
        </Alert>
      )}
    </Stack>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Box component="section" aria-label={`${title} settings`}>
      <Typography variant="h2" component="h3" sx={{ mb: 1.5 }}>
        {title}
      </Typography>
      <Stack spacing={2}>{children}</Stack>
    </Box>
  );
}
