import { Box } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { bucketsQuery, useNow } from '@/features/accounts';
import type { AccountSummary } from '@/shared/api';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { holdingsList, summaryQuery } from '../api/portfolioQueries';
import { useMarket } from '../hooks/useMarket';
import { useQuotesService } from '../hooks/useQuotesService';
import { useRefreshOnQuoteRun } from '../hooks/useRefreshOnQuoteRun';
import { useRefreshPrices } from '../hooks/useRefreshPrices';
import { usePortfolioFilters } from '../hooks/usePortfolioFilters';
import { actingAccount, isConnected, portfolioAccounts } from '../lib/accounts';
import { activeFilterCount } from '../lib/filters';
import { HoldingDrawer } from './HoldingDrawer';
import { HoldingsGrid, NoMatches } from './HoldingsGrid';
import { EmptyHoldings, NoPortfolioAccounts, NotConnectedNotice } from './NoticeCards';
import { PortfolioHeader } from './PortfolioHeader';
import { PricesLine } from './PricesLine';
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
  // While the market is open the totals and the holdings are read again every minute (not in a hidden tab).
  const market = useMarket();
  const quotes = useQuotesService(market.open);
  const refresh = useRefreshPrices(quotes.service);
  useRefreshOnQuoteRun(quotes.service);
  const summary = useQuery({ ...summaryQuery(filters), refetchInterval: market.pollMs });
  const holdings = useInfiniteRows({ ...holdingsList(filters), refetchInterval: market.pollMs });

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
        <PricesLine
          summary={summary.data}
          now={market.now}
          marketOpen={market.open}
          quotes={quotes.service}
          quotesUnknown={quotes.isError}
        />
      )}
      {summary.data && (
        <SummaryNotes
          defaultDateLots={summary.data.defaultDateLots}
          unpricedCount={summary.data.unpricedCount}
          dayChangeExcluded={summary.data.dayChangeExcluded}
          onReviewDates={() => {
            update({ status: 'open', sort: { field: 'acquiredOn', descending: false } });
          }}
        />
      )}
      <SyncBar
        account={actingTarget}
        connected={connected}
        fallbackRun={summary.data?.lastSync ?? null}
        refresh={{ available: quotes.service !== null, busy: refresh.busy, onRefresh: refresh.start }}
      />
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
        holding={holdings.rows.find((h) => h.id === openId)}
        onClose={() => {
          open(null);
        }}
      />
    </Box>
  );
}
