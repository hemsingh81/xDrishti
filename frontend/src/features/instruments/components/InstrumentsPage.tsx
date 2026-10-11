import ListAltIcon from '@mui/icons-material/ListAltOutlined';
import { Box } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useId, useRef, useState } from 'react';
import { PageHeader } from '@/shared/ui';
import { ToastProvider } from '@/shared/ui/ToastProvider';
import { facetsQuery, searchQuery } from '../api/instrumentsQueries';
import { useInstrumentFilters } from '../hooks/useInstrumentFilters';
import { useRefreshWatch } from '../hooks/useRefreshWatch';
import { activeFilterCount } from '../lib/filters';
import { EmptyMaster } from './EmptyMaster';
import { FilterBar } from './FilterBar';
import { InstrumentDrawer } from './InstrumentDrawer';
import { InstrumentResults } from './InstrumentResults';
import { RefreshHeader } from './RefreshHeader';
import { RefreshPanel } from './RefreshPanel';

/**
 * The Instruments screen. From `md` up it fills the viewport under the top bar: header and filters stay put, the list
 * scrolls inside its own card and the pagination footer stays at the bottom; below `md` the page scrolls normally.
 * The refresh details are hidden until the owner asks for them. Search state and the open instrument live in the URL.
 */
export function InstrumentsPage() {
  return (
    <ToastProvider>
      <InstrumentsContent />
    </ToastProvider>
  );
}

function InstrumentsContent() {
  const { filters, update, clear, openId, open } = useInstrumentFilters();
  const { status, startRefresh, busy, startError } = useRefreshWatch();
  const facets = useQuery(facetsQuery);
  const results = useQuery(searchQuery(filters));
  const [panelOpen, setPanelOpen] = useState(false);
  const panelId = useId();
  const refreshButton = useRef<HTMLButtonElement>(null);
  const opener = useRef<HTMLElement | null>(null);

  const openPanel = (from: HTMLElement | null) => {
    opener.current = from;
    setPanelOpen(true);
  };
  // Focus goes back to what opened the panel; if that is gone (the chip of a finished run, a menu item) to the
  // Refresh button, which stays focusable while a refresh runs.
  const closePanel = () => {
    const back = opener.current?.isConnected ? opener.current : refreshButton.current;
    back?.focus();
    opener.current = null;
    setPanelOpen(false);
  };

  // An old link can point past the last page; step back to it instead of showing an empty list.
  const page = results.data;
  useEffect(() => {
    if (page?.items.length === 0 && page.total > 0 && filters.page > 1) {
      update({ page: Math.ceil(page.total / page.pageSize) }, { replace: true });
    }
  }, [page, filters.page, update]);

  // No master list at all: only the call to action (the refresh button is in the header).
  const emptyMaster = status.data?.totalActive === 0;
  const showList = !emptyMaster && !status.isPending;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: 0, height: { md: '100%' } }}>
      <PageHeader
        dense
        title="Instruments"
        icon={ListAltIcon}
        hue="instruments"
        description="The master list of NSE shares, ETFs, indices and futures from Dhan. Search it, filter it and see what changed."
        actions={
          <RefreshHeader
            status={status}
            busy={busy}
            panelOpen={panelOpen}
            panelId={panelId}
            refreshButtonRef={refreshButton}
            onOpenPanel={openPanel}
            onStart={(force) => {
              openPanel(refreshButton.current);
              startRefresh(force);
            }}
          />
        }
      />
      {panelOpen && (
        <RefreshPanel id={panelId} status={status} busy={busy} startError={startError} onClose={closePanel} />
      )}
      {emptyMaster && <EmptyMaster />}
      {showList && (
        <>
          <FilterBar filters={filters} facets={facets.data} onChange={update} onClear={clear} />
          <InstrumentResults
            result={results}
            filters={filters}
            openId={openId}
            hasActiveFilters={activeFilterCount(filters) > 0}
            onChange={update}
            onClear={clear}
            onOpen={open}
          />
          <InstrumentDrawer
            id={openId}
            onClose={() => {
              open(null);
            }}
          />
        </>
      )}
    </Box>
  );
}
