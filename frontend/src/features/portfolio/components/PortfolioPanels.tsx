import { useState } from 'react';
import type { AccountSummary } from '@/shared/api';
import type { usePortfolioOverlays } from '../hooks/usePortfolioOverlays';
import type { AlertTarget } from '../lib/alertTarget';
import { PortfolioOverlays } from './PortfolioOverlays';

interface PortfolioPanelsProps {
  /** The panels the URL has open (see `usePortfolioOverlays`). */
  overlays: ReturnType<typeof usePortfolioOverlays>;
  /** The accounts with the Portfolio role and the page's account (null = all accounts). */
  accounts: readonly AccountSummary[];
  scopeAccountId: string | null;
  /** The alert dialog: an instrument to set it on, 'pick' to choose one, null = closed. */
  alertDialog: AlertTarget | 'pick' | null;
  onAlertDialog: (dialog: AlertTarget | 'pick' | null) => void;
}

/** The side panels and dialogs on top of the Portfolio screen, wired to the URL state. The Add trade dialog is local to it. */
export function PortfolioPanels({
  overlays,
  accounts,
  scopeAccountId,
  alertDialog,
  onAlertDialog,
}: PortfolioPanelsProps) {
  const [addingTrade, setAddingTrade] = useState(false);
  return (
    <PortfolioOverlays
      alertsTab={overlays.alertsTab}
      onAlertsTab={overlays.changeAlertsTab}
      onCloseAlerts={overlays.closeAlerts}
      dialog={alertDialog}
      onNewAlert={() => {
        onAlertDialog('pick');
      }}
      onCloseDialog={() => {
        onAlertDialog(null);
      }}
      cash={{
        open: overlays.cashOpen,
        adding: overlays.cashAdding,
        importing: overlays.cashImporting,
        suggestions: overlays.cashSuggestions,
        onImportTrades: overlays.importTradesInstead,
        onImportFiles: overlays.openImportFiles,
        onClose: overlays.closeCash,
        accounts,
        scopeAccountId,
      }}
      files={{
        open: overlays.importFilesOpen,
        onClose: overlays.closeImportFiles,
        onImportDividends: overlays.importDividendsFromFiles,
        accounts,
        scopeAccountId,
      }}
      trades={{
        tab: overlays.tradesTab,
        onTab: overlays.changeTradesTab,
        onClose: overlays.closeTrades,
        onImportDividends: overlays.importDividendsInstead,
        onImportFiles: overlays.openImportFiles,
        accounts,
        scopeAccountId,
        adding: addingTrade,
        onAdd: () => {
          setAddingTrade(true);
        },
        onCloseAdd: () => {
          setAddingTrade(false);
        },
      }}
    />
  );
}
