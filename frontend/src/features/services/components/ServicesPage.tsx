import { Box } from '@mui/material';
import { useQueryClient } from '@tanstack/react-query';
import { Suspense, useState } from 'react';
import { PortfolioRefreshDialog } from '@/features/accounts';
import { queryRoots, type ServiceView } from '@/shared/api';
import { formatIstDayAndTime } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import { servicesQuery } from '../api/servicesQueries';
import { useServiceActions } from '../hooks/useServiceActions';
import { useServicesWatch } from '../hooks/useServicesWatch';
import { problemText } from '../lib/errors';
import { ChecksBanner } from './ChecksBanner';
import { ConfigureDialog } from './ConfigureDialog';
import { HistoryDrawer } from './HistoryDrawer';
import { QueryAlert } from './QueryAlert';
import type { ServiceActionHandlers } from './ServiceActions';
import { ServiceGrid } from './ServiceGrid';
import { ServicesHeader } from './ServicesHeader';
import { SummaryStrip } from './SummaryStrip';
import { TimelineCard } from './TimelineCard';

/**
 * The Services screen: what runs by itself, when, and how the last run went. From `md` it fills the viewport under the
 * top bar: a header, the schedule checks, a summary strip, the trading-day timeline and a grid of service cards taking
 * the rest of the height (it scrolls inside); on a phone the same blocks stack and the page scrolls. Each card has Run
 * now and a menu: Configure (a dialog with a preview of the next runs and the checks), Pause / Resume and History (a
 * drawer); the account sync has Settings (a dialog) instead.
 */
export function ServicesPage() {
  return <ServicesContent />;
}

function ServicesContent() {
  const queryClient = useQueryClient();
  const services = useServicesWatch();
  const actions = useServiceActions();
  const [configuring, setConfiguring] = useState<ServiceView | null>(null);
  const [history, setHistory] = useState<ServiceView | null>(null);
  // By id, so the dialog's line about the sync follows the list when it is read again.
  const [settingsId, setSettingsId] = useState<string | null>(null);
  const items = services.data?.items;

  const handlers: ServiceActionHandlers = {
    onRun: actions.runNow,
    onConfigure: setConfiguring,
    onTogglePause: actions.togglePause,
    onHistory: setHistory,
    onSettings: (service) => {
      setSettingsId(service.id);
    },
  };
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
          <SummaryStrip services={items} />
          <TimelineCard />
          <ServiceGrid services={items} busyId={actions.busyId} handlers={handlers} />
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
      {settingsId !== null && (
        <Suspense fallback={null}>
          <PortfolioRefreshDialog
            subtitle={syncSubtitle(items?.find((s) => s.id === settingsId))}
            onClose={() => {
              setSettingsId(null);
            }}
            onSaved={() => {
              // The next run of the sync follows the earliest daily time: read the services again.
              void queryClient.invalidateQueries({ queryKey: queryRoots.services });
            }}
          />
        </Suspense>
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

/** What the services list knows of the sync: when it runs next and when it last ran. */
function syncSubtitle(service: ServiceView | undefined): string | undefined {
  if (!service) return undefined;
  const next =
    service.enabled && service.nextRun ? `Next sync ${formatIstDayAndTime(service.nextRun)} IST` : 'Nothing scheduled';
  const run = service.lastRun;
  return run ? `${next} · last run ${formatIstDayAndTime(run.finishedAt ?? run.startedAt)} IST` : next;
}
