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
import { subject } from '../lib/messages';
import { BasketCardList } from './BasketCardList';
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
 * The basket cards and the selected basket's members, inside one drag-and-drop context: rows reorder among themselves,
 * rows dropped on a card are copied there (moved with Alt / Option), cards reorder among themselves.
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
  const members = useQuery(membersQuery(selected.id, asOf));
  const items = useMemo(() => members.data?.items ?? [], [members.data]);
  const selection = useScopedSelection(`${selected.id}|${asOf ?? ''}`);
  const actions = useMemberActions({ source: selected, baskets });
  const { mutate: reorderBaskets } = useReorderBaskets();
  const { mutate: reorderMembers } = useReorderMembers();
  const editable = asOf === null;

  const symbols = useMemo(() => new Map(items.map((m) => [m.instrument.id, m.instrument.symbol])), [items]);
  const memberOrder = useMemo(() => (editable ? items.map((m) => m.instrument.id) : []), [editable, items]);
  const basketOrder = useMemo(() => baskets.map((b) => b.id), [baskets]);
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

  const dnd = useBasketDnd({ memberOrder, basketOrder, selection: selection.ids, labelOf, onOutcome });
  const drag = useMemo(
    () => ({ session: dnd.session, over: dnd.over, move: dnd.move }),
    [dnd.session, dnd.over, dnd.move],
  );
  const panel = useMemo<MemberPanel>(
    () => ({
      basket: selected,
      others: baskets.filter((b) => b.id !== selected.id),
      busy: actions.busy,
      transfer: actions.transfer,
      remove: actions.remove,
    }),
    [selected, baskets, actions],
  );

  return (
    <DndContext {...dnd.dndProps}>
      <BasketDragContext value={drag}>
        <MemberPanelContext value={panel}>
          <Box
            sx={{
              display: 'grid',
              gap: 2,
              flex: '1 1 auto',
              minHeight: 0,
              gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: 'minmax(260px, 320px) minmax(0, 1fr)' },
              gridTemplateRows: { md: 'minmax(0, 1fr)' },
            }}
          >
            {phone ? (
              <BasketSelect baskets={baskets} selectedId={selected.id} onSelect={onSelect} />
            ) : (
              <BasketCardList baskets={baskets} selectedId={selected.id} onSelect={onSelect} />
            )}
            <MembersPanel
              basket={selected}
              members={members}
              asOf={asOf}
              onAsOfChange={onAsOfChange}
              selectedIds={selection.ids}
              onSelectionChange={selection.set}
              onClear={selection.clear}
              onEdit={onEdit}
              onArchive={onArchive}
            />
          </Box>
          <DragOverlay dropAnimation={null}>
            {dnd.session && <DragPreview session={dnd.session} over={dnd.over} move={dnd.move} labelOf={labelOf} />}
          </DragOverlay>
        </MemberPanelContext>
      </BasketDragContext>
    </DndContext>
  );
}
