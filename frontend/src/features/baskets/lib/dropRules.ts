import { moveBlock } from './order';

export type DragKind = 'member' | 'basket';

/** What a draggable or droppable carries in its `data`: a member row, or a basket card. */
export interface DndData {
  type: DragKind;
  /** The basket the row belongs to, or the card's own basket. */
  basketId: string;
}

export const isDndData = (value: unknown): value is DndData =>
  typeof value === 'object' &&
  value !== null &&
  'type' in value &&
  (value.type === 'member' || value.type === 'basket') &&
  'basketId' in value &&
  typeof value.basketId === 'string';

/** What is being dragged: one basket card, or one or several members (the whole selection) of the shown basket. */
export interface DragSession {
  kind: DragKind;
  /** The grabbed row / card. */
  activeId: string;
  /** Instrument ids (members) or the basket id (a card), in their current order. */
  ids: readonly string[];
  /** The basket the members belong to; for a card, its own id. */
  sourceId: string;
}

export interface DropTarget {
  id: string;
  data: DndData;
}

export type DropOutcome =
  | { kind: 'none' }
  | { kind: 'reorder-members'; ids: string[] }
  | { kind: 'reorder-baskets'; ids: string[] }
  | { kind: 'transfer'; fromId: string; toId: string; ids: readonly string[]; mode: 'copy' | 'move' };

interface DropContext {
  memberOrder: readonly string[];
  basketOrder: readonly string[];
  /** Alt / Option is held: a drop on a basket moves instead of copying. */
  alt: boolean;
}

/**
 * What a drop means. Cards reorder among cards. Members dropped on another member reorder (as a block when several are
 * dragged); dropped on another basket's card they are copied there, moved with Alt / Option held; on their own basket's
 * card or nowhere, nothing happens.
 */
export function resolveDrop(session: DragSession, target: DropTarget | null, context: DropContext): DropOutcome {
  if (!target || target.id === session.activeId) return { kind: 'none' };
  if (session.kind === 'basket') {
    if (target.data.type !== 'basket') return { kind: 'none' };
    const ids = moveBlock(context.basketOrder, [session.activeId], session.activeId, target.id);
    return ids ? { kind: 'reorder-baskets', ids } : { kind: 'none' };
  }
  if (target.data.type === 'member') {
    if (target.data.basketId !== session.sourceId) return { kind: 'none' };
    const ids = moveBlock(context.memberOrder, session.ids, session.activeId, target.id);
    return ids ? { kind: 'reorder-members', ids } : { kind: 'none' };
  }
  if (target.id === session.sourceId) return { kind: 'none' };
  return {
    kind: 'transfer',
    fromId: session.sourceId,
    toId: target.id,
    ids: session.ids,
    mode: context.alt ? 'move' : 'copy',
  };
}

/** The drag a grabbed row / card starts: dragging a selected row (of several) takes the whole selection with it. */
export function sessionFor(
  activeId: string,
  data: unknown,
  selection: ReadonlySet<string>,
  memberOrder: readonly string[],
): DragSession | null {
  if (!isDndData(data)) return null;
  const ids =
    data.type === 'member' && selection.has(activeId) && selection.size > 1
      ? memberOrder.filter((id) => selection.has(id))
      : [activeId];
  return { kind: data.type, activeId, ids, sourceId: data.basketId };
}
