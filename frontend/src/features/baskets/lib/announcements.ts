import type { Active, Announcements, Over, ScreenReaderInstructions } from '@dnd-kit/core';
import { isDndData, type DragSession } from './dropRules';

/** What the announcements need to know about the screen. */
export interface AnnouncementContext {
  /** What the grabbed row / card carries (the whole selection for a selected row). */
  sessionOf: (active: Active) => DragSession | null;
  memberOrder: readonly string[];
  basketOrder: readonly string[];
  /** Symbol of an instrument / name of a basket. */
  labelOf: (kind: 'member' | 'basket', id: string) => string;
  /** Alt / Option is held right now. */
  isMove: () => boolean;
}

export const screenReaderInstructions: ScreenReaderInstructions = {
  draggable:
    'To pick up, press space or enter. Use the arrow keys to move over another row to reorder, or over a basket to copy; hold Alt to move instead of copy. Press space to drop or escape to cancel. The Copy to and Move to menus do the same without dragging.',
};

const position = (order: readonly string[], id: string): string => {
  const index = order.indexOf(id);
  return index < 0 ? '' : `position ${String(index + 1)} of ${String(order.length)}`;
};

function what(context: AnnouncementContext, session: DragSession): string {
  if (session.kind === 'basket') return `basket ${context.labelOf('basket', session.activeId)}`;
  return session.ids.length === 1
    ? context.labelOf('member', session.activeId)
    : `${context.labelOf('member', session.activeId)} and ${String(session.ids.length - 1)} more`;
}

/** Spoken messages for the pick up / over / drop / cancel steps of a drag. */
export function createAnnouncements(context: AnnouncementContext): Announcements {
  const describeOver = (session: DragSession, over: Over | null): string => {
    const subject = what(context, session);
    const data = over?.data.current;
    if (!over || !isDndData(data)) return `${subject} is not over a place to drop.`;
    if (data.type === 'basket' && session.kind === 'member') {
      if (String(over.id) === session.sourceId) {
        return `${subject} is over its own basket ${context.labelOf('basket', String(over.id))}. Nothing would change.`;
      }
      return `${subject} is over basket ${context.labelOf('basket', String(over.id))}. Dropping ${
        context.isMove() ? 'moves it there' : 'copies it there; hold Alt to move'
      }.`;
    }
    const order = data.type === 'basket' ? context.basketOrder : context.memberOrder;
    return `${subject} is over ${context.labelOf(data.type, String(over.id))}, ${position(order, String(over.id))}.`;
  };
  return {
    onDragStart({ active }) {
      const session = context.sessionOf(active);
      if (!session) return undefined;
      const order = session.kind === 'basket' ? context.basketOrder : context.memberOrder;
      return `Picked up ${what(context, session)}, ${position(order, String(active.id))}.`;
    },
    onDragOver({ active, over }) {
      const session = context.sessionOf(active);
      return session ? describeOver(session, over) : undefined;
    },
    onDragEnd({ active, over }) {
      const session = context.sessionOf(active);
      if (!session) return undefined;
      const data = over?.data.current;
      if (!over || !isDndData(data) || String(over.id) === session.activeId)
        return `${what(context, session)} was dropped where it was.`;
      if (data.type === 'basket' && session.kind === 'member') {
        return `Dropped ${what(context, session)} on basket ${context.labelOf('basket', String(over.id))}.`;
      }
      const order = data.type === 'basket' ? context.basketOrder : context.memberOrder;
      return `Dropped ${what(context, session)} on ${context.labelOf(data.type, String(over.id))}, ${position(order, String(over.id))}.`;
    },
    onDragCancel({ active }) {
      const session = context.sessionOf(active);
      return session ? `Cancelled. ${what(context, session)} stays where it was.` : undefined;
    },
  };
}
