import { DndContext, DragOverlay } from '@dnd-kit/core';
import { Box, useMediaQuery, useTheme } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
import type { Basket } from '@/shared/api';
import { useScopedSelection } from '@/shared/lib/useScopedSelection';
import { useToast } from '@/shared/ui/toastContext';
import { useReorderBaskets } from '../api/basketMutations';
import { basketsKeys, membersQuery } from '../api/basketsQueries';
import { useReorderMembers } from '../api/memberMutations';
import { BasketDragContext } from '../hooks/basketDragContext';
import { MemberPanelContext, type MemberPanel } from '../hooks/memberPanelContext';
import { useBasketDnd } from '../hooks/useBasketDnd';
import { useMemberActions } from '../hooks/useMemberActions';
import type { DragSession, DropOutcome } from '../lib/dropRules';
import { isConflict, isStaleOrder, reorderProblemText } from '../lib/errors';
import { splitBaskets } from '../lib/groups';
import { subject } from '../lib/messages';
import { BasketCardList } from './BasketCardList';
import { BasketInsights } from './BasketInsights';
import { BasketSelect } from './BasketSelect';
import { DragPreview } from './DragPreview';
import { MembersPanel } from './MembersPanel';

interface BasketsWorkspaceProps {
  baskets: readonly Basket[];
  selected: Basket;
  onSelect: (id: string) => void;
  asOf: string | null;
  onAsOfChange: (value: string | null) => void;
  onEdit: () => void;
  onArchive: () => void;
}

/**
 * The row of basket cards, the selected basket's members and the two insight cards beside them (below on narrower
 * screens), inside one drag-and-drop context: rows reorder among themselves,
 * rows dropped on a card are copied there (moved with Alt / Option), cards reorder among themselves. A portfolio basket
 * is read only: its rows can only be copied to the owner's baskets, and its card is neither sortable nor a drop target.
 */
export function BasketsWorkspace({
  baskets,
  selected,
  onSelect,
  asOf,
  onAsOfChange,
  onEdit,
  onArchive,
}: BasketsWorkspaceProps) {
  const theme = useTheme();
  const phone = useMediaQuery(theme.breakpoints.down('md'));
  const { notify } = useToast();
  const queryClient = useQueryClient();
  const readOnly = selected.system;
  // A portfolio basket has no look-back view: a date in the URL is kept for the owner's baskets and not sent for it.
  const shownAsOf = readOnly ? null : asOf;
  const groups = useMemo(() => splitBaskets(baskets), [baskets]);
  const members = useQuery(membersQuery(selected.id, shownAsOf));
  const items = useMemo(() => members.data?.items ?? [], [members.data]);
  const selection = useScopedSelection(`${selected.id}|${shownAsOf ?? ''}`);
  const actions = useMemberActions({ source: selected, baskets });
  const { mutate: reorderBaskets } = useReorderBaskets();
  const { mutate: reorderMembers } = useReorderMembers();
  const editable = shownAsOf === null;

  const symbols = useMemo(() => new Map(items.map((m) => [m.instrument.id, m.instrument.symbol])), [items]);
  const memberOrder = useMemo(() => (editable ? items.map((m) => m.instrument.id) : []), [editable, items]);
  const basketOrder = useMemo(() => groups.manual.map((b) => b.id), [groups]);
  const labelOf = useCallback(
    (kind: 'member' | 'basket', id: string) =>
      kind === 'basket' ? (baskets.find((b) => b.id === id)?.name ?? 'basket') : (symbols.get(id) ?? 'instrument'),
    [baskets, symbols],
  );

  const { clear } = selection;
  const onOutcome = useCallback(
    (outcome: DropOutcome, session: DragSession) => {
      const failed = (error: unknown) => {
        notify(reorderProblemText(error, 'The order could not be saved.'), 'error');
        // A basket that changed after the screen loaded refuses the old list of members / baskets: read it again.
        if (isConflict(error) || isStaleOrder(error)) void queryClient.invalidateQueries({ queryKey: basketsKeys.all });
      };
      if (outcome.kind === 'reorder-baskets') reorderBaskets(outcome.ids, { onError: failed });
      else if (outcome.kind === 'reorder-members') {
        reorderMembers({ basketId: session.sourceId, instrumentIds: outcome.ids }, { onError: failed });
      } else if (outcome.kind === 'transfer') {
        const label = subject(outcome.ids.length, symbols.get(outcome.ids[0] ?? '') ?? '');
        void actions.transfer(outcome.ids, outcome.toId, outcome.mode, label).then((done) => {
          if (done) clear();
        });
      }
    },
    [notify, queryClient, reorderBaskets, reorderMembers, actions, symbols, clear],
  );

  const dnd = useBasketDnd({ memberOrder, basketOrder, readOnly, selection: selection.ids, labelOf, onOutcome });
  const drag = useMemo(
    () => ({ session: dnd.session, over: dnd.over, move: dnd.move }),
    [dnd.session, dnd.over, dnd.move],
  );
  const panel = useMemo<MemberPanel>(
    () => ({
      basket: selected,
      others: groups.manual.filter((b) => b.id !== selected.id),
      busy: actions.busy,
      transfer: actions.transfer,
      remove: actions.remove,
    }),
    [selected, groups, actions],
  );

  return (
    <DndContext {...dnd.dndProps}>
      <BasketDragContext value={drag}>
        <MemberPanelContext value={panel}>
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, minWidth: 0 }}>
            {phone ? (
              <BasketSelect groups={groups} selectedId={selected.id} onSelect={onSelect} />
            ) : (
              <BasketCardList groups={groups} selectedId={selected.id} onSelect={onSelect} />
            )}
            <Box
              sx={{
                display: 'grid',
                gap: 2,
                alignItems: 'start',
                gridTemplateColumns: {
                  xs: 'minmax(0, 1fr)',
                  lg: 'minmax(0, 3fr) minmax(0, 2fr)',
                  xl: 'minmax(0, 1fr) 340px',
                },
              }}
            >
              <MembersPanel
                basket={selected}
                members={members}
                asOf={shownAsOf}
                onAsOfChange={onAsOfChange}
                selectedIds={selection.ids}
                onSelectionChange={selection.set}
                onClear={selection.clear}
                onEdit={onEdit}
                onArchive={onArchive}
              />
              <BasketInsights />
            </Box>
          </Box>
          <DragOverlay dropAnimation={null}>
            {dnd.session && <DragPreview session={dnd.session} over={dnd.over} move={dnd.move} labelOf={labelOf} />}
          </DragOverlay>
        </MemberPanelContext>
      </BasketDragContext>
    </DndContext>
  );
}
