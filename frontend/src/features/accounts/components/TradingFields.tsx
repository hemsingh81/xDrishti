import { Stack } from '@mui/material';
import type { RiskProfile } from '@/shared/api';
import type { FieldErrors } from '../lib/fieldErrors';
import type { TradingFormState } from '../lib/tradingForm';
import { InstrumentPicker } from './InstrumentPicker';
import { OverrideFields } from './OverrideFields';
import { TradingBasics } from './TradingBasics';

interface TradingFieldsProps {
  idPrefix: string;
  state: TradingFormState;
  errors: FieldErrors;
  profiles: RiskProfile[];
  onChange: (patch: Partial<TradingFormState>) => void;
}

/** All trading settings; used by the Trading tab and by the wizard's role-settings step. */
export function TradingFields({ idPrefix, state, errors, profiles, onChange }: TradingFieldsProps) {
  const profile = profiles.find((p) => p.id === state.riskProfileId);
  return (
    <Stack spacing={2.5}>
      <TradingBasics idPrefix={idPrefix} state={state} errors={errors} profiles={profiles} onChange={onChange} />
      <InstrumentPicker state={state} errors={errors} onChange={onChange} />
      <OverrideFields idPrefix={idPrefix} state={state} errors={errors} profile={profile} onChange={onChange} />
    </Stack>
  );
}
