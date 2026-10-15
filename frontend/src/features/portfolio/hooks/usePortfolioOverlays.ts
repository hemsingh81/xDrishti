import { useCallback } from 'react';
import { useSearchParams } from 'react-router';
import { isAlertStatus, type AlertStatus } from '../lib/alerts';
import { isTradesTab, type TradesTab } from '../lib/tradesTab';
import { usePanelParam } from './usePanelParam';

const parseAlerts = (value: string | null): AlertStatus | null => (isAlertStatus(value) ? value : null);
const parseCash = (value: string | null): 'open' | 'add' | 'import' | 'suggestions' | null =>
  value === 'open' || value === 'add' || value === 'import' || value === 'suggestions' ? value : null;
const parseTrades = (value: string | null): TradesTab | null => (isTradesTab(value) ? value : null);
const parseImportFiles = (value: string | null): 'open' | null => (value === 'open' ? 'open' : null);

/**
 * The panels that sit on top of the Portfolio screen and are addressed by the URL, so the bell (and a shared link) can lead
 * to them: `?alerts=active|triggered|cancelled` opens the Alerts panel on a tab, `?trades=import|history` the Trades panel,
 * `?cash=open` the Cash & dividends panel (`?cash=add` with its Add dividend form open, `?cash=import` with the
 * dividend import open, `?cash=suggestions` with the keyboard on the suggested dividends of the ledger),
 * `?import=open` opens the Import files dialog (Dhan's downloaded reports: transaction report, DP charges, demat holding),
 * `?focus=actions` takes the owner to the Action center once (the card removes it when it has done so), `?focus=sync` to the
 * Sync bar's trades and ledger section (the page removes it when it has done so). See `usePanelParam`
 * for how opening, closing and Back agree.
 */
export function usePortfolioOverlays() {
  const [params, setParams] = useSearchParams();
  const alerts = usePanelParam('alerts', parseAlerts);
  const trades = usePanelParam('trades', parseTrades);
  const cash = usePanelParam('cash', parseCash);
  const importFiles = usePanelParam('import', parseImportFiles);
  const focusActions = params.get('focus') === 'actions';
  const focusSync = params.get('focus') === 'sync';

  const focusHandled = useCallback(() => {
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.delete('focus');
        return next;
      },
      { replace: true },
    );
  }, [setParams]);

  /** Swaps one panel for another in the same history entry (a file dropped on the wrong import). */
  const swap = useCallback(
    (open: 'cash' | 'trades') => {
      setParams(
        (previous) => {
          const next = new URLSearchParams(previous);
          for (const param of ['cash', 'trades', 'import']) next.delete(param);
          next.set(open, 'import');
          return next;
        },
        { replace: true },
      );
    },
    [setParams],
  );

  return {
    alertsTab: alerts.value,
    openAlerts: (tab: AlertStatus = 'active') => {
      alerts.open(tab);
    },
    changeAlertsTab: alerts.change,
    closeAlerts: alerts.close,
    tradesTab: trades.value,
    openTrades: (tab: TradesTab = 'import') => {
      trades.open(tab);
    },
    changeTradesTab: trades.change,
    closeTrades: trades.close,
    cashOpen: cash.value !== null,
    /** The panel was opened to add a dividend straight away (the invitation under the tiles). */
    cashAdding: cash.value === 'add',
    openCash: () => {
      cash.open('open');
    },
    /** The panel was opened on the dividend import (a link, or a dividend file dropped on the trade import). */
    cashImporting: cash.value === 'import',
    /** The panel was opened on the suggested dividends (the bell's "Possible dividends in the ledger"). */
    cashSuggestions: cash.value === 'suggestions',
    /** Opens the dividend import in place of the Trades panel. */
    importDividendsInstead: () => {
      swap('cash');
    },
    /** Opens the trade import in place of the Cash & dividends panel. */
    importTradesInstead: () => {
      swap('trades');
    },
    importFilesOpen: importFiles.value !== null,
    /** Opens the Import files dialog (over whatever panel is open: closing it returns there). */
    openImportFiles: () => {
      importFiles.open('open');
    },
    closeImportFiles: importFiles.close,
    /** Opens the dividend import in place of the Import files dialog (a dividend list was among the files). */
    importDividendsFromFiles: () => {
      swap('cash');
    },
    /** Opens the panel on its Add dividend form. */
    openCashToAdd: () => {
      cash.open('add');
    },
    closeCash: cash.close,
    focusActions,
    focusSync,
    focusHandled,
  };
}
