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
const ImportFilesDialog = lazy(async () => ({
  default: (await import('./imports/ImportFilesDialog')).ImportFilesDialog,
}));
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
    /** A dividend file was dropped on the trade import: the dividend import opens instead. */
    onImportDividends: () => void;
    /** Opens the Import files dialog (a report was dropped on the trade import, or its header button). */
    onImportFiles: () => void;
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
    /** Opened on the dividend import. */
    importing: boolean;
    /** Opened on the suggested dividends of the ledger. */
    suggestions?: boolean;
    /** A tradebook was dropped on the dividend import: the trade import opens instead. */
    onImportTrades: () => void;
    /** Opens the Import files dialog (a report was dropped on the dividend import, or its button). */
    onImportFiles: () => void;
    onClose: () => void;
    accounts: readonly AccountSummary[];
    scopeAccountId: string | null;
  };
  /** The Import files dialog: Dhan's downloaded reports, imported together. */
  files: {
    open: boolean;
    onClose: () => void;
    /** A dividend list was among the files: the dividend import opens instead. */
    onImportDividends: () => void;
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
  files,
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
          onImportDividends={trades.onImportDividends}
          onImportFiles={trades.onImportFiles}
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
          startImporting={cash.importing}
          focusSuggestions={cash.suggestions}
          onImportTrades={cash.onImportTrades}
          onImportFiles={cash.onImportFiles}
        />
      )}
      {files.open && (
        <ImportFilesDialog
          accounts={files.accounts}
          scopeAccountId={files.scopeAccountId}
          onClose={files.onClose}
          onImportDividends={files.onImportDividends}
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
