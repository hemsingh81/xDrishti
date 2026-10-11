import { Box, Button, Stack } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useRef, useState, type SyntheticEvent } from 'react';
import type { AccountDetail, RiskProfile } from '@/shared/api';
import { ErrorState, LoadingState } from '@/shared/ui';
import { useToast } from '@/shared/ui/toastContext';
import { useUpdateTrading } from '../api/accountMutations';
import { riskProfilesQuery } from '../api/accountsQueries';
import { fieldErrorsOf, problemMessage, scoped, type FieldErrors } from '../lib/fieldErrors';
import { focusFirstInvalid } from '../lib/focus';
import {
  defaultProfileId,
  toTradingInput,
  tradingFormFrom,
  validateTrading,
  type TradingFormState,
} from '../lib/tradingForm';
import { FormAlert } from './FormAlert';
import { RoleOffNotice } from './RoleOffNotice';
import { TradingFields } from './TradingFields';

interface TradingTabProps {
  account: AccountDetail;
  onEnableRole: () => void;
}

/** Trading settings of one account: mode, risk profile, capital, styles, instruments and overrides. */
export function TradingTab({ account, onEnableRole }: TradingTabProps) {
  const profiles = useQuery(riskProfilesQuery);
  if (profiles.isPending) {
    return <LoadingState label="Loading risk profiles" />;
  }
  if (profiles.error) {
    return <ErrorState error={profiles.error} />;
  }
  return (
    <Stack spacing={2}>
      {!account.trading.enabled && <RoleOffNotice roleKey="trading" onEnable={onEnableRole} />}
      {/* The key restarts the form from the saved values after every successful save. */}
      <TradingForm key={account.updatedAt} account={account} profiles={profiles.data} />
    </Stack>
  );
}

function TradingForm({ account, profiles }: { account: AccountDetail; profiles: RiskProfile[] }) {
  const { notify } = useToast();
  const save = useUpdateTrading();
  const formRef = useRef<HTMLFormElement>(null);
  const [state, setState] = useState<TradingFormState>(() =>
    tradingFormFrom(account.trading, defaultProfileId(profiles)),
  );
  const [clientErrors, setClientErrors] = useState<FieldErrors>({});
  const errors = { ...scoped(fieldErrorsOf(save.error), 'trading'), ...clientErrors };

  function change(patch: Partial<TradingFormState>) {
    setState((previous) => ({ ...previous, ...patch }));
    setClientErrors({});
    save.reset();
  }

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    const found = validateTrading(state);
    setClientErrors(found);
    if (Object.keys(found).length > 0) {
      focusFirstInvalid(formRef.current);
      return;
    }
    save.mutate(
      { id: account.id, body: toTradingInput(state) },
      {
        onSuccess: () => {
          notify('Trading settings saved — applied from the next nightly pipeline.');
        },
        onError: () => {
          focusFirstInvalid(formRef.current);
        },
      },
    );
  }

  return (
    <Box component="form" noValidate onSubmit={submit} ref={formRef} aria-label="Trading settings">
      <Stack spacing={2.5}>
        <TradingFields idPrefix="trading" state={state} errors={errors} profiles={profiles} onChange={change} />
        <FormAlert message={problemMessage(save.error)} />
        <Box>
          <Button type="submit" variant="contained" loading={save.isPending}>
            Save trading settings
          </Button>
        </Box>
      </Stack>
    </Box>
  );
}
