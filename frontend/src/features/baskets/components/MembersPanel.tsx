import { Alert, Box, Card, Divider } from '@mui/material';
import type { UseQueryResult } from '@tanstack/react-query';
import { useState } from 'react';
import type { Basket, BasketMembers } from '@/shared/api';
import { formatDate } from '@/shared/lib/format';
import { basketProblemText } from '../lib/errors';
import { AddInstrumentRow } from './AddInstrumentRow';
import { AddInstrumentsDialog } from './AddInstrumentsDialog';
import { BasketHeader } from './BasketHeader';
import { EmptyMembers } from './EmptyMembers';
import { HistoryDrawer } from './HistoryDrawer';
import { MemberToolbar } from './MemberToolbar';
import { MembersGrid } from './MembersGrid';

interface MembersPanelProps {
  basket: Basket;
  members: UseQueryResult<BasketMembers>;
  /** A look-back date (yyyy-MM-dd), or null for the members now. */
  asOf: string | null;
  onAsOfChange: (value: string | null) => void;
  selectedIds: ReadonlySet<string>;
  onSelectionChange: (ids: ReadonlySet<string>) => void;
  onClear: () => void;
  onEdit: () => void;
  onArchive: () => void;
}

/**
 * The left card under the row of baskets: the selected basket's title and actions, the add row, the selection toolbar
 * (while something is selected) and the members grid, with the Add instruments dialog and the history drawer it opens.
 * A look-back date makes the list read only; so does a portfolio basket, which offers only Copy to….
 */
export function MembersPanel({
  basket,
  members,
  asOf,
  onAsOfChange,
  selectedIds,
  onSelectionChange,
  onClear,
  onEdit,
  onArchive,
}: MembersPanelProps) {
  // Set while the Add instruments dialog is open: the search it starts with.
  const [adding, setAdding] = useState<string | null>(null);
  const [history, setHistory] = useState(false);
  const readOnly = basket.system;
  const editable = asOf === null;
  const items = members.data?.items ?? [];
  const failed = members.isError && !members.data;
  const openAdd = (query = '') => {
    setAdding(query);
  };

  return (
    <Card sx={{ display: 'flex', flexDirection: 'column', minWidth: 0 }}>
      <BasketHeader
        basket={basket}
        asOf={asOf}
        onAsOfChange={onAsOfChange}
        onEdit={onEdit}
        onHistory={() => {
          setHistory(true);
        }}
        onArchive={onArchive}
      />
      {!readOnly && editable && <AddInstrumentRow onAdd={openAdd} />}
      {asOf && (
        <Alert severity="info" sx={{ mx: 2, mb: 1.5 }}>
          {`Showing the members as they were at the end of ${formatDate(asOf)}. This view is read only; choose Back to today to make changes.`}
        </Alert>
      )}
      <Divider />
      {editable && <MemberToolbar members={items} selectedIds={selectedIds} onClear={onClear} />}
      {/* The grid scrolls inside this box when the basket is long, so the cards above stay in reach as drop targets. */}
      <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: 0, maxHeight: 'min(68vh, 720px)' }}>
        <MembersGrid
          members={items}
          mode={readOnly ? 'portfolio' : editable ? 'edit' : 'lookback'}
          selectedIds={editable ? selectedIds : undefined}
          onSelectionChange={editable ? onSelectionChange : undefined}
          loading={members.isPending}
          error={failed ? basketProblemText(members.error, 'The members could not be loaded.') : null}
          onRetry={() => {
            void members.refetch();
          }}
          emptyState={
            <EmptyMembers
              asOf={asOf}
              readOnly={readOnly}
              onAdd={() => {
                openAdd();
              }}
            />
          }
        />
      </Box>
      {adding !== null && !readOnly && (
        <AddInstrumentsDialog
          basket={basket}
          memberIds={new Set(items.map((m) => m.instrument.id))}
          initialQuery={adding}
          onClose={() => {
            setAdding(null);
          }}
        />
      )}
      {!readOnly && (
        <HistoryDrawer
          basketId={basket.id}
          basketName={basket.name}
          open={history}
          onClose={() => {
            setHistory(false);
          }}
        />
      )}
    </Card>
  );
}
