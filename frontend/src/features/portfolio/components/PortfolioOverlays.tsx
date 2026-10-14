import { lazy, Suspense } from 'react';
import type { AccountSummary } from '@/shared/api';
import { initialTradesAccount } from '../lib/accounts';
import type { AlertStatus } from '../lib/alerts';
import type { AlertTarget } from '../lib/alertTarget';
import type { TradesTab } from '../lib/tradesTab';

// The panels and dialogs are loaded when first opened: most visits never open them.
const AlertsDrawer = lazy(async () => ({ default: (await import('./alerts/AlertsDrawer')).AlertsDrawer }));
const AlertDialog = lazy(async () => ({ default: (await import('./alerts/AlertDialog')).AlertDialog }));
const TradesDrawer = lazy(async () => ({ default: (await import('./trades/TradesDrawer')).TradesDrawer }));
const CashDrawer = lazy(async () => ({ default: (await import('./broker/CashDrawer')).CashDrawer }));
const AddTradeDialog = lazy(async () => ({ default: (await import('./trades/AddTradeDialog')).AddTradeDialog }));

export interface PortfolioOverlaysProps {
  /** The tab of the open Alerts panel; null = closed. */
  alertsTab: AlertStatus | null;
  onAlertsTab: (tab: AlertStatus) => void;
  onCloseAlerts: () => void;
  /** The alert dialog: an instrument to set it on, 'pick' to choose one, null = closed. */
  dialog: AlertTarget | 'pick' | null;
  onNewAlert: () => void;
  onCloseDialog: () => void;
  /** The Trades panel: the open tab (null = closed), the accounts it can import for and the page's account. */
  trades: {
    tab: TradesTab | null;
    onTab: (tab: TradesTab) => void;
    onClose: () => void;
    accounts: readonly AccountSummary[];
    scopeAccountId: string | null;
    /** The Add trade dialog is open. */
    adding: boolean;
    onAdd: () => void;
    onCloseAdd: () => void;
  };
  /** The Cash & dividends panel: open or not, the accounts it can show and the page's account. */
  cash: {
    open: boolean;
    /** Opened to add a dividend straight away. */
    adding: boolean;
    onClose: () => void;
    accounts: readonly AccountSummary[];
    scopeAccountId: string | null;
  };
}

/** The Alerts, Trades and Cash & dividends panels and the alert and Add trade dialogs on top of the Portfolio screen. */
export function PortfolioOverlays({
  alertsTab,
  onAlertsTab,
  onCloseAlerts,
  dialog,
  onNewAlert,
  onCloseDialog,
  trades,
  cash,
}: PortfolioOverlaysProps) {
  return (
    <Suspense fallback={null}>
      {alertsTab && (
        <AlertsDrawer tab={alertsTab} onTabChange={onAlertsTab} onClose={onCloseAlerts} onCreate={onNewAlert} />
      )}
      {dialog && <AlertDialog target={dialog === 'pick' ? null : dialog} onClose={onCloseDialog} />}
      {trades.tab && (
        <TradesDrawer
          tab={trades.tab}
          onTabChange={trades.onTab}
          onClose={trades.onClose}
          onAddTrade={trades.onAdd}
          accounts={trades.accounts}
          scopeAccountId={trades.scopeAccountId}
        />
      )}
      {cash.open && (
        <CashDrawer
          onClose={cash.onClose}
          accounts={cash.accounts}
          scopeAccountId={cash.scopeAccountId}
          startAdding={cash.adding}
        />
      )}
      {trades.adding && (
        <AddTradeDialog
          accounts={trades.accounts}
          initialAccountId={initialTradesAccount(trades.accounts, trades.scopeAccountId)}
          onClose={trades.onCloseAdd}
        />
      )}
    </Suspense>
  );
}
