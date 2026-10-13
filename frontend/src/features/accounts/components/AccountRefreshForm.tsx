import { Alert, Box, Button, Stack } from '@mui/material';
import { useState, type SyntheticEvent } from 'react';
import { Link } from 'react-router';
import type { AccountDetail } from '@/shared/api';
import { useUpdatePortfolio } from '../api/accountMutations';
import { autoRefreshFrom, refreshSummary, validateAutoRefresh, type AutoRefreshState } from '../lib/autoRefresh';
import { fieldErrorsOf, problemMessage, scoped, type FieldErrors } from '../lib/fieldErrors';
import { toStatusKey } from '../lib/connection';
import { portfolioFormFrom, toPortfolioInput } from '../lib/portfolioForm';
import { AutoRefreshSettings } from './AutoRefreshSettings';
import { FormAlert } from './FormAlert';

interface AccountRefreshFormProps {
  account: AccountDetail;
  /** The stored settings are being read again: what is shown may be out of date, so saving waits. */
  fetching?: boolean;
  onSaved?: () => void;
}

/**
 * Saving the portfolio settings also imports the holdings when the account is connected and has none yet. That first
 * import has its own choices (the date holdings get), so such an account is only shown here, not changed.
 */
const wouldImport = (account: AccountDetail): boolean =>
  !account.portfolio.hasHoldings && toStatusKey(account.connection.status) !== 'not_connected';

/** The refresh setting of one account: Off or Daily at a time, with Save. Everything else is sent back as stored. */
export function AccountRefreshForm({ account, fetching = false, onSaved }: AccountRefreshFormProps) {
  const save = useUpdatePortfolio({ onSaved });
  const stored = autoRefreshFrom(account.portfolio);
  // What the owner has typed; none until they change something, so the form follows what is stored (a background read
  // updates it) and an unsaved change is not lost when that happens.
  const [draft, setDraft] = useState<AutoRefreshState | null>(null);
  const state = draft ?? stored;
  const [clientErrors, setClientErrors] = useState<FieldErrors>({});
  const locked = wouldImport(account);
  const dirty = draft !== null && (draft.mode !== stored.mode || draft.dailyTime !== stored.dailyTime);
  const errors: FieldErrors = { ...clientErrors, ...scoped(fieldErrorsOf(save.error), 'portfolio') };

  const change = (patch: Partial<AutoRefreshState>) => {
    setDraft({ ...state, ...patch });
    setClientErrors({});
    save.reset();
  };
  const submit = (event: SyntheticEvent) => {
    event.preventDefault();
    const invalid = validateAutoRefresh(state);
    setClientErrors(invalid);
    if (Object.keys(invalid).length > 0 || locked || fetching) return;
    save.mutate(
      { id: account.id, body: toPortfolioInput({ ...portfolioFormFrom(account.portfolio), refresh: state }) },
      {
        onSuccess: () => {
          setDraft(null);
        },
      },
    );
  };

  return (
    <Box component="form" noValidate onSubmit={submit} aria-label={`Automatic refresh: ${account.name}`}>
      <Stack sx={{ gap: 1.5 }}>
        <AutoRefreshSettings
          idPrefix={`refresh-${account.id}`}
          value={state}
          errors={errors}
          disabled={locked || save.isPending}
          onChange={change}
        />
        {locked && (
          <Alert
            severity="info"
            action={
              <Button
                color="inherit"
                size="small"
                component={Link}
                to={`/accounts?account=${account.id}&tab=portfolio`}
                aria-label={`Change in Accounts: ${account.name}`}
              >
                Change in Accounts
              </Button>
            }
          >
            Currently {refreshSummary(stored)}. This account has no holdings yet, so saving here would also import them
            for the first time; choose the first import date in Accounts.
          </Alert>
        )}
        <FormAlert message={problemMessage(save.error)} />
        {!locked && (
          <Box>
            <Button
              type="submit"
              variant="contained"
              size="small"
              loading={save.isPending}
              disabled={!dirty || fetching}
              aria-label={`Save: ${account.name}`}
            >
              Save
            </Button>
          </Box>
        )}
      </Stack>
    </Box>
  );
}
