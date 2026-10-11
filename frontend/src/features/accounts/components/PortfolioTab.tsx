import { Box, Button, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useRef, useState, type SyntheticEvent } from 'react';
import type { AccountDetail, PortfolioBucket } from '@/shared/api';
import { ErrorState, LoadingState } from '@/shared/ui';
import { useConnectionState } from '../hooks/useConnectionState';
import { useUpdatePortfolio } from '../api/accountMutations';
import { bucketsQuery } from '../api/accountsQueries';
import { fieldErrorsOf, problemMessage, scoped, unplacedMessages, type FieldErrors } from '../lib/fieldErrors';
import { focusFirstInvalid } from '../lib/focus';
import { portfolioFormFrom, toPortfolioInput, validatePortfolio, type PortfolioFormState } from '../lib/portfolioForm';
import { BucketAdder } from './BucketAdder';
import { BucketList } from './BucketList';
import { FormAlert } from './FormAlert';
import { PortfolioFields } from './PortfolioFields';
import { RoleOffNotice } from './RoleOffNotice';

/** Portfolio settings of one account: default bucket, inclusion in totals, auto refresh and the first import date. */
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

/** The fields of the API's portfolio settings that this form shows. */
const shownFields = ['defaultBucket', 'refreshMode', 'refreshDailyTimeIst', 'firstImportDate'];

function PortfolioForm({ account, buckets }: { account: AccountDetail; buckets: PortfolioBucket[] }) {
  const save = useUpdatePortfolio();
  const formRef = useRef<HTMLFormElement>(null);
  const [state, setState] = useState<PortfolioFormState>(() => portfolioFormFrom(account.portfolio));
  const [clientErrors, setClientErrors] = useState<FieldErrors>({});
  const connected = useConnectionState(account.connection).status === 'connected';
  const errors: FieldErrors = { ...clientErrors, ...scoped(fieldErrorsOf(save.error), 'portfolio') };
  const ownBucketName = account.portfolio.autoBucketName;

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
    <Stack spacing={2.5}>
      <Box component="form" noValidate onSubmit={submit} ref={formRef} aria-label="Portfolio settings">
        <Stack spacing={2.5}>
          <PortfolioFields
            idPrefix="portfolio"
            state={state}
            errors={errors}
            buckets={buckets}
            ownBucketName={ownBucketName}
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
      <Box>
        <Typography variant="h3" sx={{ mb: 1 }}>
          Buckets
        </Typography>
        <BucketList ownBucketName={ownBucketName} buckets={buckets} />
        <BucketAdder />
      </Box>
    </Stack>
  );
}
