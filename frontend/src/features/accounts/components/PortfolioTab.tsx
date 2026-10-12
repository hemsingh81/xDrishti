import { Box, Button, Stack } from '@mui/material';
import { useRef, useState, type SyntheticEvent } from 'react';
import type { AccountDetail } from '@/shared/api';
import { useConnectionState } from '../hooks/useConnectionState';
import { useUpdatePortfolio } from '../api/accountMutations';
import { fieldErrorsOf, problemMessage, scoped, unplacedMessages, type FieldErrors } from '../lib/fieldErrors';
import { focusFirstInvalid } from '../lib/focus';
import { portfolioFormFrom, toPortfolioInput, validatePortfolio, type PortfolioFormState } from '../lib/portfolioForm';
import { FormAlert } from './FormAlert';
import { PortfolioFields } from './PortfolioFields';
import { RoleOffNotice } from './RoleOffNotice';

/** Portfolio settings of one account: the first import date, inclusion in totals and the auto refresh. */
export function PortfolioTab({ account, onEnableRole }: { account: AccountDetail; onEnableRole: () => void }) {
  return (
    <Stack spacing={2}>
      {!account.portfolio.enabled && <RoleOffNotice roleKey="portfolio" onEnable={onEnableRole} />}
      <PortfolioForm key={account.updatedAt} account={account} />
    </Stack>
  );
}

/** The fields of the API's portfolio settings that this form shows. */
const shownFields = ['refreshMode', 'refreshDailyTimeIst', 'firstImportDate'];

function PortfolioForm({ account }: { account: AccountDetail }) {
  const save = useUpdatePortfolio();
  const formRef = useRef<HTMLFormElement>(null);
  const [state, setState] = useState<PortfolioFormState>(() => portfolioFormFrom(account.portfolio));
  const [clientErrors, setClientErrors] = useState<FieldErrors>({});
  const connected = useConnectionState(account.connection).status === 'connected';
  const errors: FieldErrors = { ...clientErrors, ...scoped(fieldErrorsOf(save.error), 'portfolio') };

  function change(patch: Partial<PortfolioFormState>) {
    setState((previous) => ({ ...previous, ...patch }));
    setClientErrors({});
    save.reset();
  }

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    const invalid = validatePortfolio(state);
    setClientErrors(invalid);
    if (Object.keys(invalid).length > 0) {
      focusFirstInvalid(formRef.current);
      return;
    }
    save.mutate(
      { id: account.id, body: toPortfolioInput(state) },
      {
        onError: () => {
          focusFirstInvalid(formRef.current);
        },
      },
    );
  }

  return (
    <Box component="form" noValidate onSubmit={submit} ref={formRef} aria-label="Portfolio settings">
      <Stack spacing={2.5}>
        <PortfolioFields
          idPrefix="portfolio"
          state={state}
          errors={errors}
          firstImport={{ hasHoldings: account.portfolio.hasHoldings, connected }}
          onChange={change}
        />
        <FormAlert message={problemMessage(save.error) ?? unplacedMessages(errors, shownFields)} />
        <Box>
          <Button type="submit" variant="contained" loading={save.isPending}>
            Save portfolio settings
          </Button>
        </Box>
      </Stack>
    </Box>
  );
}
