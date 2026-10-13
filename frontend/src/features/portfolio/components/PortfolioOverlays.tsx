import { lazy, Suspense } from 'react';
import type { AlertStatus } from '../lib/alerts';
import type { AlertTarget } from '../lib/alertTarget';

// The alert panel and dialog are loaded when first opened: most visits never open them.
const AlertsDrawer = lazy(async () => ({ default: (await import('./alerts/AlertsDrawer')).AlertsDrawer }));
const AlertDialog = lazy(async () => ({ default: (await import('./alerts/AlertDialog')).AlertDialog }));

export interface PortfolioOverlaysProps {
  /** The tab of the open Alerts panel; null = closed. */
  alertsTab: AlertStatus | null;
  onAlertsTab: (tab: AlertStatus) => void;
  onCloseAlerts: () => void;
  /** The alert dialog: an instrument to set it on, 'pick' to choose one, null = closed. */
  dialog: AlertTarget | 'pick' | null;
  onNewAlert: () => void;
  onCloseDialog: () => void;
}

/** The Alerts panel and the alert dialog on top of the Portfolio screen. */
export function PortfolioOverlays({
  alertsTab,
  onAlertsTab,
  onCloseAlerts,
  dialog,
  onNewAlert,
  onCloseDialog,
}: PortfolioOverlaysProps) {
  return (
    <Suspense fallback={null}>
      {alertsTab && (
        <AlertsDrawer tab={alertsTab} onTabChange={onAlertsTab} onClose={onCloseAlerts} onCreate={onNewAlert} />
      )}
      {dialog && <AlertDialog target={dialog === 'pick' ? null : dialog} onClose={onCloseDialog} />}
    </Suspense>
  );
}
