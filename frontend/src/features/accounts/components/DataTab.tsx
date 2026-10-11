import { Box, Button, Stack, TextField } from '@mui/material';
import { useState, type SyntheticEvent } from 'react';
import type { AccountDetail } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { useUpdateData } from '../api/accountMutations';
import { fieldErrorsOf, problemMessage, scoped } from '../lib/fieldErrors';
import { maxLiveFeedBudget, validateLiveFeedBudget } from '../lib/portfolioForm';
import { DisabledActions } from './DisabledActions';
import { Facts } from './Facts';
import { FormAlert } from './FormAlert';
import { RoleOffNotice } from './RoleOffNotice';

/** Data role: primary/standby explanation, live-feed budget, and the not-yet-available subscription facts. */
export function DataTab({ account, onEnableRole }: { account: AccountDetail; onEnableRole: () => void }) {
  const { data } = account;
  return (
    <Stack spacing={2.5}>
      {!data.enabled && <RoleOffNotice roleKey="data" onEnable={onEnableRole} />}
      <Facts
        items={[
          {
            label: 'Role',
            value: data.primary ? (
              <>
                <strong>Primary</strong> — all market-data services use this account
              </>
            ) : data.standby ? (
              'Standby — takes over if the primary fails (automatic failover arrives with P1-12)'
            ) : (
              'Not a Data account'
            ),
          },
          {
            label: 'Data API subscription',
            value: 'Unknown until the broker adapter exists (P1-04)',
          },
          { label: 'Last EOD fetch', value: '—' },
        ]}
      />
      <BudgetForm key={account.updatedAt} account={account} />
      <DisabledActions
        actions={[
          {
            label: 'Simulate failover',
            reason: 'Failover testing needs the market-data services (P1-12), which are not built yet.',
          },
        ]}
      />
    </Stack>
  );
}

function BudgetForm({ account }: { account: AccountDetail }) {
  const { notify } = useToast();
  const save = useUpdateData();
  const [budget, setBudget] = useState(String(account.data.liveFeedBudget));
  const [clientError, setClientError] = useState<string | undefined>();
  const error = clientError ?? scoped(fieldErrorsOf(save.error), 'data').liveFeedBudget;

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    const invalid = validateLiveFeedBudget(budget);
    setClientError(invalid);
    if (invalid) {
      return;
    }
    save.mutate(
      { id: account.id, liveFeedBudget: Number(budget) },
      {
        onSuccess: () => {
          notify('Data settings saved.');
        },
      },
    );
  }

  return (
    <Box component="form" noValidate onSubmit={submit} aria-label="Data settings">
      <Stack spacing={2}>
        <TextField
          label="Live-feed budget (instruments)"
          type="number"
          value={budget}
          onChange={(e) => {
            setBudget(e.target.value);
            setClientError(undefined);
            save.reset();
          }}
          error={Boolean(error)}
          helperText={
            error ?? `Dhan allows 5 × 5,000 instruments; at most ${maxLiveFeedBudget.toLocaleString('en-IN')} here.`
          }
          sx={{ maxWidth: 360 }}
          slotProps={{ htmlInput: { min: 0, max: maxLiveFeedBudget, step: 1, inputMode: 'numeric' } }}
        />
        <FormAlert message={problemMessage(save.error)} />
        <Box>
          <Button type="submit" variant="contained" loading={save.isPending}>
            Save data settings
          </Button>
        </Box>
      </Stack>
    </Box>
  );
}
