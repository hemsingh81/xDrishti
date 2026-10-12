import { Alert, Stack, Typography } from '@mui/material';
import { formatDate, formatInr } from '@/shared/lib/format';
import { dhanApp } from '../../lib/dhanApp';
import { modeLabel, roleInfo } from '../../lib/labels';
import { maskClientId, type WizardState } from '../../lib/wizardState';
import { Facts, type Fact } from '@/shared/ui';
import { FormAlert } from '../FormAlert';

/** Secrets are summarised as "•••• set" — the review never shows a value, not even partly. */
function brokerFacts(state: WizardState): Fact[] {
  const { app } = state;
  return [
    { label: 'Broker', value: dhanApp.name },
    { label: dhanApp.clientIdLabel, value: <span className="mono">{maskClientId(app.clientId.trim())}</span> },
    { label: dhanApp.apiKeyLabel, value: app.apiKey.trim() ? '•••• set' : 'not set' },
    { label: dhanApp.apiSecretLabel, value: app.apiSecret.trim() ? '•••• set' : 'not set' },
    { label: 'API key generated on', value: formatDate(app.apiKeyIssuedOn) },
  ];
}

function summary(state: WizardState): Fact[] {
  const { identity, roles } = state;
  const chosen = [
    roles.data && `${roleInfo.data.label} (${roles.dataKind})`,
    roles.trading && roleInfo.trading.label,
    roles.portfolio && roleInfo.portfolio.label,
  ].filter((role) => role !== false);
  const facts: Fact[] = [
    { label: 'Account', value: identity.name.trim() },
    ...brokerFacts(state),
    { label: 'Roles', value: chosen.join(', ') || 'none' },
  ];
  if (roles.trading) {
    const t = state.trading;
    const capital = Number(t.capital);
    facts.push({
      label: 'Trading',
      value: [
        modeLabel(t.mode),
        t.capital && Number.isFinite(capital) ? formatInr(capital) : 'no capital',
        [t.intraday && 'intraday', t.swing && 'swing'].filter((s) => s !== false).join(' + '),
      ].join(' · '),
    });
  }
  if (roles.portfolio) {
    facts.push({ label: 'First import date', value: formatDate(state.portfolio.firstImportDate) });
  }
  if (roles.data) {
    facts.push({ label: 'Live-feed budget', value: state.liveFeedBudget });
  }
  return facts;
}

interface ReviewStepProps {
  state: WizardState;
  message: string | undefined;
}

/** Step 5: everything in one place before the password is asked for. */
export function ReviewStep({ state, message }: ReviewStepProps) {
  return (
    <Stack spacing={2}>
      <Typography color="text.secondary">Check the details, then create the account.</Typography>
      <Facts items={summary(state)} />
      <Alert role="status" severity="info">
        After you confirm with your password the secrets are stored encrypted. <strong>Create and connect</strong> then
        opens {dhanApp.name}&apos;s sign-in page right away; or connect later from the account&apos;s Connection tab.
        Services start using it on their next run.
      </Alert>
      <FormAlert message={message} />
    </Stack>
  );
}
