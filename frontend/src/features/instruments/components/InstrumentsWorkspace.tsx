import { Box } from '@mui/material';
import { useState } from 'react';
import type { InfiniteRows } from '@/shared/lib/useInfiniteRows';
import { useScopedSelection } from '@/shared/lib/useScopedSelection';
import { AddToBasketDialog } from '@/features/baskets';
import { useToast } from '@/shared/ui/toastContext';
import type { InstrumentFacets, InstrumentSummary } from '@/shared/api';
import { useSelectAllIds } from '../api/selectAllMutation';
import type { useInstrumentFilters } from '../hooks/useInstrumentFilters';
import { useTrackingActions } from '../hooks/trackingActionsContext';
import { trackingProblemText } from '../lib/errors';
import { activeFilterCount, selectionScope } from '../lib/filters';
import { BulkBar } from './BulkBar';
import { FilterBar } from './FilterBar';
import { InstrumentDrawer } from './InstrumentDrawer';
import { InstrumentResults } from './InstrumentResults';
import { panelId, tabId } from '../lib/tabIds';
import { InstrumentTabs } from './InstrumentTabs';
import { PasteSymbolsDialog } from './PasteSymbolsDialog';
import { ResultsEmpty } from './ResultsEmpty';
import { TrackedNote } from './TrackedNote';

interface InstrumentsWorkspaceProps {
  /** The page state in the URL. */
  filterState: ReturnType<typeof useInstrumentFilters>;
  /** Undefined while loading or when the counts could not be loaded. */
  facets: InstrumentFacets | undefined;
  results: InfiniteRows<InstrumentSummary>;
}

/** Tabs, filters, bulk bar, list, drawer and the paste dialog: everything under the page header. */
export function InstrumentsWorkspace({ filterState, facets, results }: InstrumentsWorkspaceProps) {
  const { filters, update, clear, openId, open } = filterState;
  const selection = useScopedSelection(selectionScope(filters));
  const { notify } = useToast();
  const { track, untrack, busy } = useTrackingActions();
  const selectAll = useSelectAllIds();
  const [pasteOpen, setPasteOpen] = useState(false);
  const [basketsOpen, setBasketsOpen] = useState(false);

  const { rows, total } = results;
  const allOnPage = rows.length > 0 && rows.every((row) => selection.ids.has(row.id));
  const selectedIds = [...selection.ids];
  const count = selectedIds.length;
  const label = `${String(count)} ${count === 1 ? 'instrument' : 'instruments'}`;

  const readAllIds = () => {
    selectAll.mutate(
      { filters, total },
      {
        onSuccess: (ids) => {
          selection.set(new Set(ids));
        },
        onError: (error) => {
          notify(trackingProblemText(error, 'Not all results could be selected.'), 'error');
        },
      },
    );
  };
  const run = async (action: typeof track) => {
    if (await action(selectedIds, label)) selection.clear();
  };

  return (
    <>
      <InstrumentTabs
        value={filters.tab}
        facets={facets}
        onChange={(tab) => {
          update({ tab });
        }}
      />
      <Box
        role="tabpanel"
        id={panelId}
        aria-labelledby={tabId(filters.tab)}
        sx={{ display: 'flex', flexDirection: 'column', flex: '1 1 auto', minHeight: 0 }}
      >
        <FilterBar filters={filters} facets={facets} onChange={update} onClear={clear} />
        {filters.tab === 'tracked' && <TrackedNote items={rows} />}
        <InstrumentResults
          result={results}
          filters={filters}
          openId={openId}
          selectedIds={selection.ids}
          onSelectionChange={selection.set}
          onChange={update}
          onOpen={open}
          toolbar={
            <BulkBar
              selected={count}
              pageRows={rows.length}
              allOnPageSelected={allOnPage}
              total={total}
              selectingAll={selectAll.isPending}
              busy={busy}
              onSelectAll={readAllIds}
              onClear={selection.clear}
              onTrack={() => {
                void run(track);
              }}
              onUntrack={() => {
                void run(untrack);
              }}
              onPaste={() => {
                setPasteOpen(true);
              }}
              onAddToBasket={() => {
                setBasketsOpen(true);
              }}
            />
          }
          emptyState={
            <ResultsEmpty
              tab={filters.tab}
              hasActiveFilters={activeFilterCount(filters) > 0}
              onClear={clear}
              onShowMaster={() => {
                update({ tab: 'master' });
              }}
              onPaste={() => {
                setPasteOpen(true);
              }}
            />
          }
        />
      </Box>
      <InstrumentDrawer
        id={openId}
        onClose={() => {
          open(null);
        }}
      />
      {basketsOpen && (
        <AddToBasketDialog
          instrumentIds={selectedIds}
          label={label}
          onClose={() => {
            setBasketsOpen(false);
          }}
          onAdded={() => {
            setBasketsOpen(false);
            selection.clear();
          }}
        />
      )}
      <PasteSymbolsDialog
        open={pasteOpen}
        onClose={() => {
          setPasteOpen(false);
        }}
        onTracked={() => {
          setPasteOpen(false);
          selection.clear();
        }}
      />
    </>
  );
}
