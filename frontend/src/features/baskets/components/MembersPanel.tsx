import { Alert, Card, Divider } from '@mui/material';
import type { UseQueryResult } from '@tanstack/react-query';
import { useState } from 'react';
import type { Basket, BasketMembers } from '@/shared/api';
import { formatDate } from '@/shared/lib/format';
import { basketProblemText } from '../lib/errors';
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
 * The right-hand card: the selected basket's header and actions, the selection toolbar and the members grid, with the
 * Add instruments dialog and the history drawer it opens. A look-back date makes the list read only.
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
  const [adding, setAdding] = useState(false);
  const [history, setHistory] = useState(false);
  const editable = asOf === null;
  const items = members.data?.items ?? [];
  const failed = members.isError && !members.data;
  const openAdd = () => {
    setAdding(true);
  };

  return (
    <Card sx={{ display: 'flex', flexDirection: 'column', minHeight: { md: 0 }, minWidth: 0 }}>
      <BasketHeader
        basket={basket}
        asOf={asOf}
        onAsOfChange={onAsOfChange}
        onAdd={openAdd}
        onEdit={onEdit}
        onHistory={() => {
          setHistory(true);
        }}
        onArchive={onArchive}
      />
      {asOf && (
        <Alert severity="info" sx={{ mx: 2, mb: 1.5 }}>
          {`Showing the members as they were at the end of ${formatDate(asOf)}. This view is read only; choose Back to today to make changes.`}
        </Alert>
      )}
      <Divider />
      {editable && (
        <>
          <MemberToolbar members={items} selectedIds={selectedIds} onClear={onClear} />
          <Divider />
        </>
      )}
      <MembersGrid
        members={items}
        editable={editable}
        selectedIds={editable ? selectedIds : undefined}
        onSelectionChange={editable ? onSelectionChange : undefined}
        loading={members.isPending}
        error={failed ? basketProblemText(members.error, 'The members could not be loaded.') : null}
        onRetry={() => {
          void members.refetch();
        }}
        emptyState={<EmptyMembers asOf={asOf} onAdd={openAdd} />}
      />
      {adding && (
        <AddInstrumentsDialog
          basket={basket}
          memberIds={new Set(items.map((m) => m.instrument.id))}
          onClose={() => {
            setAdding(false);
          }}
        />
      )}
      <HistoryDrawer
        basketId={basket.id}
        basketName={basket.name}
        open={history}
        onClose={() => {
          setHistory(false);
        }}
      />
    </Card>
  );
}
