import { Box } from '@mui/material';
import { useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import type { ServiceView } from '@/shared/api';
import { LoadingState } from '@/shared/ui';
import { ToastProvider } from '@/shared/ui/ToastProvider';
import { servicesQuery } from '../api/servicesQueries';
import { useServiceActions } from '../hooks/useServiceActions';
import { useServicesWatch } from '../hooks/useServicesWatch';
import { problemText } from '../lib/errors';
import { ChecksBanner } from './ChecksBanner';
import { ConfigureDialog } from './ConfigureDialog';
import { HistoryDrawer } from './HistoryDrawer';
import { QueryAlert } from './QueryAlert';
import type { ServiceActionHandlers } from './ServiceActions';
import { ServicesCard } from './ServicesCard';
import { ServicesHeader } from './ServicesHeader';
import { TimelineCard } from './TimelineCard';

/**
 * The Services screen: what runs by itself, when, and how the last run went. From `md` it fills the viewport under the
 * top bar: a slim header, the schedule checks, the trading-day timeline (compact) and the services table taking the
 * rest of the height (its rows scroll inside); on a phone each service is a card. Run now, Configure (a dialog with a
 * preview of the next runs and the checks), Pause / Resume and History (a drawer) are on every service.
 */
export function ServicesPage() {
  return (
    <ToastProvider>
      <ServicesContent />
    </ToastProvider>
  );
}

function ServicesContent() {
  const queryClient = useQueryClient();
  const services = useServicesWatch();
  const actions = useServiceActions();
  const [configuring, setConfiguring] = useState<ServiceView | null>(null);
  const [history, setHistory] = useState<ServiceView | null>(null);
  const items = services.data?.items;

  const { runNow, togglePause } = actions;
  const handlers = useMemo<ServiceActionHandlers>(
    () => ({ onRun: runNow, onConfigure: setConfiguring, onTogglePause: togglePause, onHistory: setHistory }),
    [runNow, togglePause],
  );
  const reload = async () => {
    const fresh = await queryClient.query({ ...servicesQuery, staleTime: 0 });
    setConfiguring((current) => fresh.items.find((s) => s.id === current?.id) ?? null);
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: 0, height: { md: '100%' } }}>
      <ServicesHeader />
      {services.isPending && <LoadingState label="Loading services" />}
      {services.isError && !items && (
        <QueryAlert
          message={problemText(services.error, 'The services could not be loaded.')}
          onRetry={() => {
            void services.refetch();
          }}
        />
      )}
      {items && (
        <>
          <ChecksBanner services={items} />
          <TimelineCard />
          <ServicesCard services={items} busyId={actions.busyId} handlers={handlers} />
        </>
      )}
      {configuring && items && (
        <ConfigureDialog
          key={`${configuring.id}:${String(configuring.revision)}`}
          service={configuring}
          services={items}
          onClose={() => {
            setConfiguring(null);
          }}
          onReload={() => {
            void reload();
          }}
        />
      )}
      <HistoryDrawer
        service={history}
        onClose={() => {
          setHistory(null);
        }}
      />
    </Box>
  );
}
