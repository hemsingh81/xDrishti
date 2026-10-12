import {
  closestCenter,
  KeyboardSensor,
  MouseSensor,
  pointerWithin,
  TouchSensor,
  useSensor,
  useSensors,
  type CollisionDetection,
  type DndContextProps,
  type DragEndEvent,
  type DragOverEvent,
  type DragStartEvent,
} from '@dnd-kit/core';
import { sortableKeyboardCoordinates } from '@dnd-kit/sortable';
import { useCallback, useMemo, useState } from 'react';
import { createAnnouncements, screenReaderInstructions } from '../lib/announcements';
import {
  isDndData,
  resolveDrop,
  sessionFor,
  type DragSession,
  type DropOutcome,
  type DropTarget,
} from '../lib/dropRules';
import { useAltKey } from './useAltKey';

/** Pointer: the droppable under the pointer (a basket card or a member row). Keyboard: the nearest one. */
const collisionDetection: CollisionDetection = (args) => {
  // A basket card is only ever dropped among the cards.
  const cardsOnly = args.active.data.current?.type === 'basket';
  const next = cardsOnly
    ? { ...args, droppableContainers: args.droppableContainers.filter((c) => c.data.current?.type === 'basket') }
    : args;
  return next.pointerCoordinates ? pointerWithin(next) : closestCenter(next);
};

interface BasketDndOptions {
  /** The shown basket's members in their order (empty while a past date is shown: rows cannot be dragged then). */
  memberOrder: readonly string[];
  basketOrder: readonly string[];
  /** The shown basket is a portfolio basket: its rows are only copied (Alt is ignored) and never reordered. */
  readOnly?: boolean;
  /** Selected instrument ids: dragging a selected row drags all of them. */
  selection: ReadonlySet<string>;
  labelOf: (kind: 'member' | 'basket', id: string) => string;
  onOutcome: (outcome: DropOutcome, session: DragSession) => void;
}

export interface BasketDnd {
  /** Spread on `DndContext`. */
  dndProps: Pick<
    DndContextProps,
    'sensors' | 'collisionDetection' | 'accessibility' | 'onDragStart' | 'onDragOver' | 'onDragEnd' | 'onDragCancel'
  >;
  session: DragSession | null;
  /** What the drag is over now. */
  over: DropTarget | null;
  /** Alt / Option is held: a drop on a basket moves. */
  move: boolean;
}

const targetOf = (event: DragOverEvent | DragEndEvent): DropTarget | null => {
  const over = event.over;
  const data: unknown = over?.data.current;
  return over && isDndData(data) ? { id: String(over.id), data } : null;
};

/**
 * Drag and drop of the Baskets screen: sensors for mouse, touch and keyboard, what is under the pointer, what a drop
 * means (`resolveDrop`) and the spoken announcements. It decides nothing about the data: the caller gets the outcome.
 */
export function useBasketDnd(options: BasketDndOptions): BasketDnd {
  const { memberOrder, basketOrder, readOnly = false, selection, labelOf, onOutcome } = options;
  const [session, setSession] = useState<DragSession | null>(null);
  const [over, setOver] = useState<DropTarget | null>(null);
  const alt = useAltKey(session !== null);
  const sensors = useSensors(
    useSensor(MouseSensor, { activationConstraint: { distance: 6 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 200, tolerance: 8 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  const { isHeld } = alt;
  const announcements = useMemo(
    () =>
      createAnnouncements({
        sessionOf: (active) => sessionFor(String(active.id), active.data.current, selection, memberOrder),
        memberOrder,
        basketOrder,
        labelOf,
        isMove: () => !readOnly && isHeld(),
      }),
    [selection, memberOrder, basketOrder, labelOf, isHeld, readOnly],
  );

  const onDragStart = useCallback(
    (event: DragStartEvent) => {
      const next = sessionFor(String(event.active.id), event.active.data.current, selection, memberOrder);
      if (!next) return;
      setSession(next);
      setOver(null);
    },
    [selection, memberOrder],
  );

  const onDragOver = useCallback((event: DragOverEvent) => {
    setOver(targetOf(event));
  }, []);

  const finish = useCallback(() => {
    setSession(null);
    setOver(null);
  }, []);

  const onDragEnd = useCallback(
    (event: DragEndEvent) => {
      const current = session;
      const target = targetOf(event);
      const move = alt.isHeld();
      finish();
      if (current) onOutcome(resolveDrop(current, target, { memberOrder, basketOrder, alt: move, readOnly }), current);
    },
    [session, alt, finish, onOutcome, memberOrder, basketOrder, readOnly],
  );

  return {
    dndProps: {
      sensors,
      collisionDetection,
      accessibility: { announcements, screenReaderInstructions },
      onDragStart,
      onDragOver,
      onDragEnd,
      onDragCancel: finish,
    },
    session,
    over,
    move: alt.held && !readOnly,
  };
}
