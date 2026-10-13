import { useCallback } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router';
import { isAlertStatus, type AlertStatus } from '../lib/alerts';

/** Remembered in the history entry that opening the Alerts panel added, so closing it can take that entry away again. */
interface PanelState {
  alertsPushed?: boolean;
}

const pushedHere = (state: unknown): boolean =>
  typeof state === 'object' && state !== null && (state as PanelState).alertsPushed === true;

/**
 * The panels that sit on top of the Portfolio screen and are addressed by the URL, so the bell (and a shared link) can lead
 * to them: `?alerts=active|triggered|cancelled` opens the Alerts panel on a tab, `?focus=actions` takes the owner to the
 * Action center once (the card removes it when it has done so). Other parameters (the filters, the open holding) are kept.
 *
 * Opening the panel from this screen adds a history entry and closing it goes back one step, so Back and Close agree (one
 * press each); a panel that was opened by a link (the bell, a bookmark) is closed by replacing its entry.
 */
export function usePortfolioOverlays() {
  const [params, setParams] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();
  const alerts = params.get('alerts');
  const alertsTab: AlertStatus | null = isAlertStatus(alerts) ? alerts : null;
  const focusActions = params.get('focus') === 'actions';
  const pushed = pushedHere(location.state);

  const openAlerts = useCallback(
    (tab: AlertStatus = 'active') => {
      setParams(
        (previous) => {
          const next = new URLSearchParams(previous);
          next.set('alerts', tab);
          return next;
        },
        { state: { alertsPushed: true } satisfies PanelState },
      );
    },
    [setParams],
  );

  const changeAlertsTab = useCallback(
    (tab: AlertStatus) => {
      setParams(
        (previous) => {
          const next = new URLSearchParams(previous);
          next.set('alerts', tab);
          return next;
        },
        // A tab is a step within the panel, not a page of its own; the entry keeps its mark.
        { replace: true, state: location.state as unknown },
      );
    },
    [setParams, location.state],
  );

  const closeAlerts = useCallback(() => {
    if (pushed) {
      void navigate(-1);
      return;
    }
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        next.delete('alerts');
        return next;
      },
      { replace: true },
    );
  }, [pushed, navigate, setParams]);

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

  return { alertsTab, openAlerts, changeAlertsTab, closeAlerts, focusActions, focusHandled };
}
