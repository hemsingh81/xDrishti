import AddIcon from '@mui/icons-material/Add';
import PeopleIcon from '@mui/icons-material/PeopleOutlined';
import { Button } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useSearchParams } from 'react-router';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { accountsQuery } from '../api/accountsQueries';
import { parseTab, type AccountTab } from '../lib/tabs';
import { AccountsBanner } from './AccountsBanner';
import { AccountsView } from './AccountsView';
import { AddAccountWizard } from './AddAccountWizard';
import { ConnectReturn } from './ConnectReturn';
import { EmptyAccounts } from './EmptyAccounts';

/**
 * The Accounts screen: the list and the selected account's detail; account and tab live in the URL. Shared risk
 * profiles and the role matrix are on the Settings page.
 */
export function AccountsPage() {
  const { data: accounts, error, isPending, refetch } = useQuery(accountsQuery);
  const [params, setParams] = useSearchParams();
  const [wizardOpen, setWizardOpen] = useState(false);
  const tab = parseTab(params.get('tab'));
  const selectedId = accounts?.find((a) => a.id === params.get('account'))?.id ?? accounts?.[0]?.id;

  function update(patch: Record<string, string | null>) {
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        for (const [key, value] of Object.entries(patch)) {
          if (value === null) {
            next.delete(key);
          } else {
            next.set(key, value);
          }
        }
        return next;
      },
      { replace: true },
    );
  }

  const hrefFor = (id: string) => {
    const next = new URLSearchParams(params);
    next.set('account', id);
    return `?${next.toString()}`;
  };

  return (
    <>
      <ConnectReturn />
      <PageHeader
        title="Accounts"
        icon={PeopleIcon}
        hue="accounts"
        description="All your broker accounts, what each is used for, and their trading, portfolio, data and connection settings. Shared risk profiles and the role matrix are under Settings."
        actions={
          <Button
            variant="contained"
            startIcon={<AddIcon />}
            onClick={() => {
              setWizardOpen(true);
            }}
          >
            Add account
          </Button>
        }
      />
      {isPending && <LoadingState label="Loading accounts" />}
      {error && (
        <>
          <ErrorState error={error} />
          <Button
            variant="outlined"
            onClick={() => {
              void refetch();
            }}
          >
            Try again
          </Button>
        </>
      )}
      {accounts && (
        <>
          <AccountsBanner accounts={accounts} />
          {accounts.length === 0 && (
            <EmptyAccounts
              onAdd={() => {
                setWizardOpen(true);
              }}
            />
          )}
          {selectedId && (
            <AccountsView
              accounts={accounts}
              selectedId={selectedId}
              tab={tab}
              hrefFor={hrefFor}
              onTabChange={(next: AccountTab) => {
                update({ tab: next === 'overview' ? null : next });
              }}
              onRemoved={() => {
                update({ account: null, tab: null });
              }}
            />
          )}
          {wizardOpen && (
            <AddAccountWizard
              accounts={accounts}
              onClose={() => {
                setWizardOpen(false);
              }}
              onCreated={(id) => {
                setWizardOpen(false);
                update({ account: id, tab: null });
              }}
            />
          )}
        </>
      )}
    </>
  );
}
