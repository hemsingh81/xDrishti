import ListAltIcon from '@mui/icons-material/ListAltOutlined';
import { Box } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useId, useRef, useState } from 'react';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { PageHeader } from '@/shared/ui';
import { facetsQuery, searchList } from '../api/instrumentsQueries';
import { useInstrumentFilters } from '../hooks/useInstrumentFilters';
import { useRefreshWatch } from '../hooks/useRefreshWatch';
import { EmptyMaster } from './EmptyMaster';
import { InstrumentsWorkspace } from './InstrumentsWorkspace';
import { RefreshHeader } from './RefreshHeader';
import { RefreshPanel } from './RefreshPanel';
import { TrackedCount } from './TrackedCount';
import { TrackingActionsProvider } from './TrackingActionsProvider';

/**
 * The Instruments screen. From `md` up it fills the viewport under the top bar: header and filters stay put, the list
 * scrolls inside its own card (further pages load as it is scrolled) and the "Showing N of total" footer stays at the bottom; below `md` the page scrolls normally.
 * The refresh details are hidden until the owner asks for them. Search state and the open instrument live in the URL.
 */
export function InstrumentsPage() {
  return (
    <TrackingActionsProvider>
      <InstrumentsContent />
    </TrackingActionsProvider>
  );
}

function InstrumentsContent() {
  const { status, startRefresh, busy, startError } = useRefreshWatch();
  const facets = useQuery(facetsQuery);
  // The list loads in parallel with the refresh status, not after it.
  const filterState = useInstrumentFilters();
  const results = useInfiniteRows(searchList(filterState.filters));
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
        description="The master list of NSE shares, ETFs, indices and futures from Dhan. Search it, filter it, and track the ones the system should work on."
        actions={
          <>
            <TrackedCount facets={facets.data} />
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
          </>
        }
      />
      {panelOpen && (
        <RefreshPanel id={panelId} status={status} busy={busy} startError={startError} onClose={closePanel} />
      )}
      {emptyMaster && <EmptyMaster />}
      {showList && <InstrumentsWorkspace filterState={filterState} facets={facets.data} results={results} />}
    </Box>
  );
}
