import ReceiptIcon from '@mui/icons-material/ReceiptLongOutlined';
import { Box, Button } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useRef, useState } from 'react';
import { bucketsQuery, useNow } from '@/features/accounts';
import type { AccountSummary } from '@/shared/api';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { holdingsList, summaryQuery } from '../api/portfolioQueries';
import type { HoldingRef } from '../api/lookupQueries';
import { useDrawerHolding } from '../hooks/useDrawerHolding';
import { useMarket } from '../hooks/useMarket';
import { usePortfolioHistory } from '../hooks/usePortfolioHistory';
import { useQuotesService } from '../hooks/useQuotesService';
import { useRefreshOnQuoteRun } from '../hooks/useRefreshOnQuoteRun';
import { useCachedHolding } from '../hooks/useCachedHolding';
import { useRefreshPrices } from '../hooks/useRefreshPrices';
import { usePortfolioFilters } from '../hooks/usePortfolioFilters';
import { usePortfolioOverlays } from '../hooks/usePortfolioOverlays';
import { actingAccount, isConnected, portfolioAccounts } from '../lib/accounts';
import type { AlertTarget } from '../lib/alertTarget';
import { alertTargetOfHolding } from '../lib/alertTarget';
import { activeFilterCount } from '../lib/filters';
import { ActionCenter } from './actions/ActionCenter';
import { HistoryCard } from './HistoryCard';
import { GroupedHoldings } from './GroupedHoldings';
import { HoldingDrawer } from './HoldingDrawer';
import { HoldingsGrid, NoMatches } from './HoldingsGrid';
import { EmptyHoldings, NoGroups, NoPortfolioAccounts, NotConnectedNotice } from './NoticeCards';
import { PortfolioHeader } from './PortfolioHeader';
import { PortfolioOverlays } from './PortfolioOverlays';
import { PricesLine } from './PricesLine';
import { ScopeBar } from './ScopeBar';
import { SummaryNotes } from './SummaryNotes';
import { SummaryTiles } from './SummaryTiles';
import { SyncBar } from './SyncBar';

/** The page once the accounts are known: scope, totals, value history, sync bar, holdings and the holding drawer. */
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
  // The flat list is for Group by None; a grouping loads the groups and, per opened group, its holdings.
  const grouped = filters.groupBy !== 'none';
  const holdings = useInfiniteRows({ ...holdingsList(filters), enabled: !grouped, refetchInterval: market.pollMs });
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
  const [alertDialog, setAlertDialog] = useState<AlertTarget | 'pick' | null>(null);
  const [addingTrade, setAddingTrade] = useState(false);
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
  const scopeLabel =
    [
      acting && account !== null ? acting.name : null,
      filters.bucket === null ? null : (buckets.data?.find((b) => b.id === filters.bucket)?.name ?? null),
    ]
      .filter((part): part is string => part !== null)
      .join(' · ') || null;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: 0, height: { md: '100%' } }}>
      <PortfolioHeader
        actions={
          <Button
            variant="outlined"
            startIcon={<ReceiptIcon />}
            onClick={() => {
              overlays.openTrades();
            }}
          >
            Trades
          </Button>
        }
      />
      {acting && !connected && <NotConnectedNotice accountId={acting.id} accountName={acting.name} />}
      <ScopeBar
        filters={filters}
        accounts={portfolio}
        buckets={buckets.data}
        bucketsLoading={buckets.isPending}
        onChange={update}
      />
      <SummaryTiles query={summary} history={{ query: history.query, rangeLabel: history.range }} />
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
            // The grouped holdings are below the cards: take the owner to them.
            requestAnimationFrame(() => {
              if (typeof holdingsStart.current?.scrollIntoView === 'function') {
                holdingsStart.current.scrollIntoView({ block: 'start' });
              }
            });
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
      <SyncBar
        account={actingTarget}
        connected={connected}
        fallbackRun={summary.data?.lastSync ?? null}
        refresh={{ available: quotes.service !== null, busy: refresh.busy, onRefresh: refresh.start }}
      />
      <div ref={holdingsStart} aria-hidden />
      {filters.groupBy === 'none' ? (
        <HoldingsGrid
          result={holdings}
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
          scope={{ filters, by: filters.groupBy, pollMs: market.pollMs, showAccount, openId, onOpen: open }}
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
      <PortfolioOverlays
        alertsTab={overlays.alertsTab}
        onAlertsTab={overlays.changeAlertsTab}
        onCloseAlerts={overlays.closeAlerts}
        dialog={alertDialog}
        onNewAlert={() => {
          setAlertDialog('pick');
        }}
        onCloseDialog={() => {
          setAlertDialog(null);
        }}
        trades={{
          tab: overlays.tradesTab,
          onTab: overlays.changeTradesTab,
          onClose: overlays.closeTrades,
          accounts: portfolio,
          scopeAccountId: account,
          adding: addingTrade,
          onAdd: () => {
            setAddingTrade(true);
          },
          onCloseAdd: () => {
            setAddingTrade(false);
          },
        }}
      />
    </Box>
  );
}
