import { Box, Button, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useRef, useState, type SyntheticEvent } from 'react';
import type { AccountDetail, PortfolioBucket } from '@/shared/api';
import { ErrorState, LoadingState } from '@/shared/ui';
import { useToast } from '@/shared/ui/toastContext';
import { useUpdatePortfolio } from '../api/accountMutations';
import { bucketsQuery } from '../api/accountsQueries';
import { fieldErrorsOf, problemMessage, scoped, type FieldErrors } from '../lib/fieldErrors';
import { focusFirstInvalid } from '../lib/focus';
import { portfolioFormFrom, toPortfolioInput, validatePortfolio, type PortfolioFormState } from '../lib/portfolioForm';
import { BucketAdder } from './BucketAdder';
import { FormAlert } from './FormAlert';
import { PortfolioFields } from './PortfolioFields';
import { RoleOffNotice } from './RoleOffNotice';

/** Portfolio settings of one account: default bucket, history start, inclusion in totals. */
export function PortfolioTab({ account, onEnableRole }: { account: AccountDetail; onEnableRole: () => void }) {
  const buckets = useQuery(bucketsQuery);
  if (buckets.isPending) {
    return <LoadingState label="Loading buckets" />;
  }
  if (buckets.error) {
    return <ErrorState error={buckets.error} />;
  }
  return (
    <Stack spacing={2}>
      {!account.portfolio.enabled && <RoleOffNotice roleKey="portfolio" onEnable={onEnableRole} />}
      <PortfolioForm key={account.updatedAt} account={account} buckets={buckets.data} />
    </Stack>
  );
}

function PortfolioForm({ account, buckets }: { account: AccountDetail; buckets: PortfolioBucket[] }) {
  const { notify } = useToast();
  const save = useUpdatePortfolio();
  const formRef = useRef<HTMLFormElement>(null);
  const [state, setState] = useState<PortfolioFormState>(() =>
    portfolioFormFrom(account.portfolio, buckets.some((b) => b.name === 'Trading') ? 'Trading' : ''),
  );
  const [clientErrors, setClientErrors] = useState<FieldErrors>({});
  const errors = { ...scoped(fieldErrorsOf(save.error), 'portfolio'), ...clientErrors };

  function change(patch: Partial<PortfolioFormState>) {
    setState((previous) => ({ ...previous, ...patch }));
    setClientErrors({});
    save.reset();
  }

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    const found = validatePortfolio(state);
    setClientErrors(found);
    if (Object.keys(found).length > 0) {
      focusFirstInvalid(formRef.current);
      return;
    }
    save.mutate(
      { id: account.id, body: toPortfolioInput(state) },
      {
        onSuccess: () => {
          notify('Portfolio settings saved.');
        },
        onError: () => {
          focusFirstInvalid(formRef.current);
        },
      },
    );
  }

  return (
    <Stack spacing={2.5}>
      <Box component="form" noValidate onSubmit={submit} ref={formRef} aria-label="Portfolio settings">
        <Stack spacing={2.5}>
          <PortfolioFields idPrefix="portfolio" state={state} errors={errors} buckets={buckets} onChange={change} />
          <FormAlert message={problemMessage(save.error)} />
          <Box>
            <Button type="submit" variant="contained" loading={save.isPending}>
              Save portfolio settings
            </Button>
          </Box>
        </Stack>
      </Box>
      <Box>
        <Typography variant="h3" sx={{ mb: 1 }}>
          Buckets
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          Available: {buckets.map((b) => b.name).join(', ')}
        </Typography>
        <BucketAdder
          onAdded={(name) => {
            change({ defaultBucket: state.defaultBucket || name });
          }}
        />
      </Box>
    </Stack>
  );
}
