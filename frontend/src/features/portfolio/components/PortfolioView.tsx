import { Box } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useMemo, useRef, useState } from 'react';
import { bucketsQuery, useNow } from '@/features/accounts';
import type { AccountSummary } from '@/shared/api';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { holdingsList, summaryQuery } from '../api/portfolioQueries';
import type { HoldingRef } from '../api/lookupQueries';
import { useDrawerHolding } from '../hooks/useDrawerHolding';
import { useLiveScope } from '../hooks/useLiveFeed';
import { useLivePolling } from '../hooks/useLivePolling';
import { useMarket } from '../hooks/useMarket';
import { usePortfolioHistory } from '../hooks/usePortfolioHistory';
import { useQuotesService } from '../hooks/useQuotesService';
import { useRefreshOnQuoteRun } from '../hooks/useRefreshOnQuoteRun';
import { useCachedHolding } from '../hooks/useCachedHolding';
import { useRefreshPrices } from '../hooks/useRefreshPrices';
import { useSyncBarFocus } from '../hooks/useSyncBarFocus';
import { useSyncFocusRequest } from '../hooks/useSyncFocusRequest';
import { useUnmatchedParam, useUnmatchedView } from '../hooks/useUnmatchedView';
import { usePortfolioFilters } from '../hooks/usePortfolioFilters';
import { usePortfolioOverlays } from '../hooks/usePortfolioOverlays';
import { actingAccount, isConnected, portfolioAccounts } from '../lib/accounts';
import { alertTargetOfHolding, type AlertTarget } from '../lib/alertTarget';
import { activeFilterCount } from '../lib/filters';
import { scopeLabelOf, scrollToElement } from '../lib/scope';
import { ActionCenter } from './actions/ActionCenter';
import { HistoryCard } from './HistoryCard';
import { LiveProvider } from './LiveProvider';
import { LiveSummary } from './LiveSummary';
import { GroupedHoldings } from './GroupedHoldings';
import { HoldingDrawer } from './HoldingDrawer';
import { HoldingsGrid, NoMatches } from './HoldingsGrid';
import { EmptyHoldings, NoGroups, NoPortfolioAccounts, NotConnectedNotice } from './NoticeCards';
import { PortfolioHeader, PortfolioHeaderActions } from './PortfolioHeader';
import { PortfolioPanels } from './PortfolioPanels';
import { ScopeBar } from './ScopeBar';
import { SyncBar } from './SyncBar';
import { UnmatchedBanner } from './UnmatchedBanner';

/** The page once the accounts are known: scope, totals, value history, sync bar, holdings and the holding drawer. */
export function PortfolioView({ accounts }: { accounts: readonly AccountSummary[] }) {
  const { filters: requested, update, clear, openId, open } = usePortfolioFilters();

  const portfolio = portfolioAccounts(accounts);
  // A link to an account that has no Portfolio role (any more) means "all accounts".
  const account = portfolio.some((a) => a.id === requested.account) ? requested.account : null;
  // The holdings with no match in the master list are a flat list: a grouping is set aside while they are shown.
  const unmatchedOn = useUnmatchedParam();
  const filters = { ...requested, account, groupBy: unmatchedOn ? ('none' as const) : requested.groupBy };
  const acting = actingAccount(portfolio, account);
  const now = useNow(acting !== null, acting?.connection.expiresAt ?? null);
  const connected = acting !== null && isConnected(acting, now);

  const buckets = useQuery(bucketsQuery);
  // While the market is open the totals and the holdings are read again every minute (not in a hidden tab).
  const market = useMarket();
  const quotes = useQuotesService(market.open);
  const refresh = useRefreshPrices(quotes.service);
  useRefreshOnQuoteRun(quotes.service);
  // The live hub (pre-open to the close, page visible): while it follows the screen, prices move by themselves and the
  // figures are read every 5 minutes; without it they are read once a minute, as before.
  const { live, liveScope, listPollMs, totalsPollMs } = useLivePolling(
    market,
    filters,
    portfolio.map((a) => a.id),
  );
  const summary = useQuery({ ...summaryQuery(filters), refetchInterval: totalsPollMs });
  // The flat list is for Group by None; a grouping loads the groups and, per opened group, its holdings.
  const grouped = filters.groupBy !== 'none';
  const holdings = useInfiniteRows({ ...holdingsList(filters), enabled: !grouped, refetchInterval: listPollMs });
  const unmatched = useUnmatchedView(unmatchedOn, holdings);
  const shownIds = useMemo(
    () => [...(grouped ? [] : holdings.rows.map((h) => h.id)), ...(openId === null ? [] : [openId])],
    [grouped, holdings.rows, openId],
  );
  useLiveScope({ enabled: live.enabled, account: filters.account, bucket: filters.bucket, holdingIds: shownIds });
  const groupedHolding = useCachedHolding(grouped ? openId : null);
  // The Action center opens holdings that the list may not have loaded: the item names them, the drawer looks them up.
  const [openHint, setOpenHint] = useState<HoldingRef | null>(null);
  const drawerHolding = useDrawerHolding(
    openId,
    grouped ? groupedHolding : holdings.rows.find((h) => h.id === openId),
    openHint,
  );
  const overlays = usePortfolioOverlays();
  const holdingsStart = useRef<HTMLDivElement>(null);
  const [syncBarRef, accountInputRef, focusSyncBar] = useSyncBarFocus();
  // Arriving from the bell on a "rows dropped" item (`?focus=sync`): scroll to the sync bar's trades and ledger and focus it.
  useSyncFocusRequest(overlays.focusSync, focusSyncBar, overlays.focusHandled);
  const [alertDialog, setAlertDialog] = useState<AlertTarget | 'pick' | null>(null);
  const scope = { account: filters.account, bucket: filters.bucket };
  const history = usePortfolioHistory(scope, market.pollMs);

  if (portfolio.length === 0) {
    return (
      <>
        <PortfolioHeader />
        <NoPortfolioAccounts />
      </>
    );
  }

  const nothingAtAll = summary.data?.openCount === 0 && summary.data.closedCount === 0 && filters.q.trim() === '';
  // While the totals of a new scope load, the card stays if the history already knows holdings (it does not blink away).
  const hasHoldings = summary.data
    ? summary.data.openCount + summary.data.closedCount > 0
    : (history.query.data?.coverage.holdings ?? 0) > 0;
  const actingTarget = acting ? { id: acting.id, name: acting.name } : null;
  const showAccount = account === null && portfolio.length > 1;
  const scopeLabel = scopeLabelOf(acting && account !== null ? acting.name : null, filters.bucket, buckets.data);

  return (
    <LiveProvider feed={live} scope={liveScope}>
      <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: 0, height: { md: '100%' } }}>
        <PortfolioHeader
          actions={<PortfolioHeaderActions onCash={overlays.openCash} onTrades={overlays.openTrades} />}
        />
        {acting && !connected && <NotConnectedNotice accountId={acting.id} accountName={acting.name} />}
        <ScopeBar
          filters={filters}
          accounts={portfolio}
          buckets={buckets.data}
          bucketsLoading={buckets.isPending}
          accountInputRef={accountInputRef}
          onChange={update}
        />
        <LiveSummary
          summary={summary}
          history={{ query: history.query, rangeLabel: history.range }}
          market={market}
          quotes={quotes.service}
          quotesUnknown={quotes.isError}
          feed={live}
          onReviewDates={() => {
            update({ status: 'open', sort: { field: 'acquiredOn', descending: false } });
          }}
          income={{ onAddDividend: overlays.openCashToAdd, onReadLedger: focusSyncBar }}
        />
        {hasHoldings && (
          <ActionCenter
            scope={scope}
            scopeLabel={scopeLabel}
            pollMs={market.pollMs}
            focusRequested={overlays.focusActions}
            onFocusHandled={overlays.focusHandled}
            onOpenHolding={(holding) => {
              setOpenHint(holding);
              open(holding.id);
            }}
            onShowSector={(accountId) => {
              update({ groupBy: 'sector', account: accountId });
              scrollToElement(holdingsStart.current); // the grouped holdings are below the cards
            }}
            onSetAlert={setAlertDialog}
            onOpenAlerts={() => {
              overlays.openAlerts();
            }}
          />
        )}
        {hasHoldings && (
          <HistoryCard
            query={history.query}
            range={history.range}
            onRangeChange={history.setRange}
            defaultDateLots={summary.data?.defaultDateLots ?? 0}
            scope={scope}
          />
        )}
        <div ref={syncBarRef} tabIndex={-1}>
          <SyncBar
            account={actingTarget}
            connected={connected}
            fallbackRun={summary.data?.lastSync ?? null}
            refresh={{ available: quotes.service !== null, busy: refresh.busy, onRefresh: refresh.start }}
            onImportFiles={overlays.openImportFiles}
          />
        </div>
        <div ref={holdingsStart} aria-hidden />
        {unmatchedOn && (
          <UnmatchedBanner count={unmatched.count} reading={unmatched.reading} onShowAll={unmatched.showAll} />
        )}
        {filters.groupBy === 'none' ? (
          <HoldingsGrid
            result={unmatched.result}
            filters={filters}
            openId={openId}
            showAccount={showAccount}
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
        ) : (
          <GroupedHoldings
            by={filters.groupBy}
            scope={{ filters, by: filters.groupBy, pollMs: totalsPollMs, showAccount, openId, onOpen: open }}
            emptyState={
              nothingAtAll ? (
                <EmptyHoldings accountId={acting?.id ?? null} />
              ) : (
                <NoGroups canClear={activeFilterCount(filters) > 0} onClear={clear} />
              )
            }
            onChange={update}
          />
        )}
        <HoldingDrawer
          id={openId}
          holding={drawerHolding}
          onClose={() => {
            open(null);
          }}
          onSetAlert={(holding) => {
            const target = alertTargetOfHolding(holding);
            if (target) setAlertDialog(target);
          }}
          onShowAlerts={() => {
            overlays.openAlerts();
          }}
        />
        <PortfolioPanels
          overlays={overlays}
          accounts={portfolio}
          scopeAccountId={account}
          alertDialog={alertDialog}
          onAlertDialog={setAlertDialog}
        />
      </Box>
    </LiveProvider>
  );
}
