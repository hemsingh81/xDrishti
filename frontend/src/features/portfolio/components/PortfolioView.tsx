import { Box } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import { bucketsQuery, useNow } from '@/features/accounts';
import type { AccountSummary } from '@/shared/api';
import { holdingsQuery, summaryQuery } from '../api/portfolioQueries';
import { usePortfolioFilters } from '../hooks/usePortfolioFilters';
import { actingAccount, isConnected, portfolioAccounts } from '../lib/accounts';
import { activeFilterCount } from '../lib/filters';
import { HoldingDrawer } from './HoldingDrawer';
import { HoldingsGrid, NoMatches } from './HoldingsGrid';
import { EmptyHoldings, NoPortfolioAccounts, NotConnectedNotice } from './NoticeCards';
import { PortfolioHeader } from './PortfolioHeader';
import { ScopeBar } from './ScopeBar';
import { SummaryNotes } from './SummaryNotes';
import { SummaryTiles } from './SummaryTiles';
import { SyncBar } from './SyncBar';

/** The page once the accounts are known: scope, totals, sync bar, holdings, the and the holding drawer. */
export function PortfolioView({ accounts }: { accounts: readonly AccountSummary[] }) {
  const { filters: requested, update, clear, openId, open } = usePortfolioFilters();

  const portfolio = portfolioAccounts(accounts);
  // A link to an account that has no Portfolio role (any more) means "all accounts".
  const account = portfolio.some((a) => a.id === requested.account) ? requested.account : null;
  const filters = { ...requested, account };
  const acting = actingAccount(portfolio, account);
  const now = useNow(acting !== null, acting?.connection.expiresAt ?? null);
  const connected = acting !== null && isConnected(acting, now);

  const buckets = useQuery(bucketsQuery);
  const summary = useQuery(summaryQuery(filters));
  const holdings = useQuery(holdingsQuery(filters));

  // An old link can point past the last page; step back to it instead of showing an empty list.
  const page = holdings.data;
  useEffect(() => {
    if (page?.items.length === 0 && page.total > 0 && filters.page > 1) {
      update({ page: Math.ceil(page.total / page.pageSize) }, { replace: true });
    }
  }, [page, filters.page, update]);

  if (portfolio.length === 0) {
    return (
      <>
        <PortfolioHeader />
        <NoPortfolioAccounts />
      </>
    );
  }

  const nothingAtAll = summary.data?.openCount === 0 && summary.data.closedCount === 0 && filters.q.trim() === '';
  const actingTarget = acting ? { id: acting.id, name: acting.name } : null;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: 0, height: { md: '100%' } }}>
      <PortfolioHeader />
      {acting && !connected && <NotConnectedNotice accountId={acting.id} accountName={acting.name} />}
      <ScopeBar
        filters={filters}
        accounts={portfolio}
        buckets={buckets.data}
        bucketsLoading={buckets.isPending}
        onChange={update}
      />
      <SummaryTiles query={summary} />
      {summary.data && (
        <SummaryNotes
          defaultDateLots={summary.data.defaultDateLots}
          unpricedCount={summary.data.unpricedCount}
          onReviewDates={() => {
            update({ status: 'open', sort: { field: 'acquiredOn', descending: false } });
          }}
        />
      )}
      <SyncBar account={actingTarget} connected={connected} fallbackRun={summary.data?.lastSync ?? null} />
      <HoldingsGrid
        result={holdings}
        filters={filters}
        openId={openId}
        showAccount={account === null && portfolio.length > 1}
        emptyState={
          nothingAtAll ? (
            <EmptyHoldings accountId={acting?.id ?? null} />
          ) : (
            <NoMatches canClear={activeFilterCount(filters) > 0} onClear={clear} />
          )
        }
        onChange={update}
        onOpen={open}
      />
      <HoldingDrawer
        id={openId}
        holding={holdings.data?.items.find((h) => h.id === openId)}
        onClose={() => {
          open(null);
        }}
      />
    </Box>
  );
}
