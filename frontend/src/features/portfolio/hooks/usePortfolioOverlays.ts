import { useCallback } from 'react';
import { useSearchParams } from 'react-router';
import { isAlertStatus, type AlertStatus } from '../lib/alerts';
import { isTradesTab, type TradesTab } from '../lib/tradesTab';
import { usePanelParam } from './usePanelParam';

const parseAlerts = (value: string | null): AlertStatus | null => (isAlertStatus(value) ? value : null);
const parseCash = (value: string | null): 'open' | 'add' | null => (value === 'open' || value === 'add' ? value : null);
const parseTrades = (value: string | null): TradesTab | null => (isTradesTab(value) ? value : null);

/**
 * The panels that sit on top of the Portfolio screen and are addressed by the URL, so the bell (and a shared link) can lead
 * to them: `?alerts=active|triggered|cancelled` opens the Alerts panel on a tab, `?trades=import|history` the Trades panel,
 * `?cash=open` the Cash & dividends panel (`?cash=add` with its Add dividend form open),
 * `?focus=actions` takes the owner to the Action center once (the card removes it when it has done so). See `usePanelParam`
 * for how opening, closing and Back agree.
 */
export function usePortfolioOverlays() {
  const [params, setParams] = useSearchParams();
  const alerts = usePanelParam('alerts', parseAlerts);
  const trades = usePanelParam('trades', parseTrades);
  const cash = usePanelParam('cash', parseCash);
  const focusActions = params.get('focus') === 'actions';

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
    /** Opens the panel on its Add dividend form. */
    openCashToAdd: () => {
      cash.open('add');
    },
    closeCash: cash.close,
    focusActions,
    focusHandled,
  };
}
