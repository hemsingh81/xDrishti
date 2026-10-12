import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable';
import type { ReactNode } from 'react';
import { useCallback, useMemo } from 'react';
import type { BasketMember } from '@/shared/api';
import { Grid, type GridRowExtras } from '@/shared/ui/grid';
import { stateAlpha } from '@/theme';
import { useBasketDrag } from '../hooks/basketDragContext';
import { editableColumns, lookBackColumns, portfolioColumns } from './memberColumns';

interface MembersGridProps {
  members: readonly BasketMember[];
  /**
   * `edit`: the owner's basket now (grip, copy / move / remove). `lookback`: a past date, read only. `portfolio`: a
   * portfolio basket, read only apart from copying out (quantity and account instead of the added date).
   */
  mode: 'edit' | 'lookback' | 'portfolio';
  /** Selection is on when `onSelectionChange` is given. */
  selectedIds?: ReadonlySet<string>;
  onSelectionChange?: (ids: ReadonlySet<string>) => void;
  loading: boolean;
  error: string | null;
  onRetry: () => void;
  emptyState: ReactNode;
}

// One object per look, shared by all rows in it: the grid's rows are memoised and re-render only when theirs changes.
const draggingRow: GridRowExtras = { sx: { opacity: stateAlpha.busy }, attributes: { 'data-drag': 'dragging' } };
const dropAbove: GridRowExtras = {
  sx: (theme) => ({ '& > td': { boxShadow: `inset 0 2px 0 0 ${theme.palette.primary.main}` } }),
  attributes: { 'data-drop': 'above' },
};
const dropBelow: GridRowExtras = {
  sx: (theme) => ({ '& > td': { boxShadow: `inset 0 -2px 0 0 ${theme.palette.primary.main}` } }),
  attributes: { 'data-drop': 'below' },
};

const columns = { edit: editableColumns, lookback: lookBackColumns, portfolio: portfolioColumns } as const;

const getRowId = (member: BasketMember) => member.instrument.id;
const getRowLabel = (member: BasketMember) => member.instrument.symbol;

/**
 * The members of a basket in their order. In the editable view every row is a sortable item; while a drag runs the rows
 * being dragged are dimmed and the row it is over shows a line where the drop would land.
 */
export function MembersGrid({
  members,
  mode,
  selectedIds,
  onSelectionChange,
  loading,
  error,
  onRetry,
  emptyState,
}: MembersGridProps) {
  const { session, over } = useBasketDrag();
  const ids = useMemo(() => members.map(getRowId), [members]);
  const dragged = useMemo(() => new Set(session?.kind === 'member' ? session.ids : []), [session]);

  const getRowProps = useCallback(
    (member: BasketMember): GridRowExtras | undefined => {
      if (session?.kind !== 'member') return undefined;
      const id = getRowId(member);
      if (dragged.has(id)) return draggingRow;
      if (mode === 'portfolio') return undefined;
      if (over?.data.type !== 'member' || over.id !== id) return undefined;
      return ids.indexOf(id) > ids.indexOf(session.activeId) ? dropBelow : dropAbove;
    },
    [session, dragged, over, ids, mode],
  );

  const grid = (
    <Grid
      label="Basket members"
      columns={columns[mode]}
      rows={members}
      getRowId={getRowId}
      getRowLabel={getRowLabel}
      selectedIds={selectedIds}
      onSelectionChange={onSelectionChange}
      getRowProps={getRowProps}
      loading={loading}
      error={error}
      onRetry={onRetry}
      emptyState={emptyState}
    />
  );
  return mode !== 'lookback' ? (
    <SortableContext items={ids} strategy={verticalListSortingStrategy}>
      {grid}
    </SortableContext>
  ) : (
    grid
  );
}
