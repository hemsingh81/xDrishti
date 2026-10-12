import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';
import type { Basket } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { basketsKeys, membersQuery } from '../api/basketsQueries';
import { useAddMembers, useRemoveMembers, useReorderMembers, useTransferMembers } from '../api/memberMutations';
import { basketProblemText, isConflict } from '../lib/errors';
import { removedMessage, subject, transferMessage } from '../lib/messages';
import { mergeOrder, sameOrder } from '../lib/order';

interface MemberActionsOptions {
  /** The basket whose members are shown. */
  source: Basket;
  baskets: readonly Basket[];
}

export interface MemberActions {
  /** A call is running: the buttons say so and ignore clicks. */
  busy: boolean;
  /** Copies or moves; a toast tells what happened and offers Undo. Resolves true when something was done. */
  transfer: (ids: readonly string[], toBasketId: string, mode: 'copy' | 'move', label: string) => Promise<boolean>;
  /** Removes from the shown basket; a toast offers Undo. Resolves true when something was done. */
  remove: (ids: readonly string[], label: string) => Promise<boolean>;
}

/** A basket's members as they were: their order and the note each was added with. */
interface Roster {
  order: string[];
  notes: ReadonlyMap<string, string | null>;
}

interface Snapshot {
  source: Roster;
  target: ReadonlySet<string>;
}

/** Instrument ids grouped by their note, in order of first appearance (one add call can carry only one note). */
function groupByNote(ids: readonly string[], notes: ReadonlyMap<string, string | null> | undefined) {
  const groups = new Map<string | null, string[]>();
  for (const id of ids) {
    const note = notes?.get(id) ?? null;
    groups.set(note, [...(groups.get(note) ?? []), id]);
  }
  return [...groups];
}

/**
 * The actions on the members of the shown basket that come with a toast and an Undo: copy / move to another basket and
 * remove. Undo is honest: it removes only what the action added, puts moved or removed instruments back (with a new
 * added date, because the stay in the basket was ended, but with their notes) and restores the order where the baskets
 * still allow it. When the
 * situation before the action could not be read, the toast says the action cannot be undone.
 */
export function useMemberActions({ source, baskets }: MemberActionsOptions): MemberActions {
  const queryClient = useQueryClient();
  const { notify } = useToast();
  const { mutateAsync: transferCall, isPending: transferring } = useTransferMembers();
  const { mutateAsync: addCall, isPending: adding } = useAddMembers();
  const { mutateAsync: removeCall, isPending: removing } = useRemoveMembers();
  const { mutateAsync: reorderCall } = useReorderMembers();
  const sourceId = source.id;
  const nameOf = useCallback((id: string) => baskets.find((b) => b.id === id)?.name ?? 'the basket', [baskets]);

  /** The members of a basket now (order and notes), read fresh from the server. */
  const rosterOf = useCallback(
    async (basketId: string): Promise<Roster> => {
      const { items } = await queryClient.query({ ...membersQuery(basketId, null), staleTime: 0 });
      return {
        order: items.map((item) => item.instrument.id),
        notes: new Map(items.map((item) => [item.instrument.id, item.note])),
      };
    },
    [queryClient],
  );
  const orderOf = useCallback(async (basketId: string) => (await rosterOf(basketId)).order, [rosterOf]);

  /** Adds instruments back to a basket with the notes they had (one call per distinct note). */
  const addBack = useCallback(
    async (basketId: string, ids: readonly string[], notes: ReadonlyMap<string, string | null> | undefined) => {
      for (const [note, group] of groupByNote(ids, notes)) {
        await addCall({ basketId, instrumentIds: group, note: note ?? undefined });
      }
    },
    [addCall],
  );

  const fail = useCallback(
    (error: unknown, fallback: string) => {
      notify(basketProblemText(error, fallback), 'error');
      // A lost race leaves the screen stale: read everything again.
      if (isConflict(error)) void queryClient.invalidateQueries({ queryKey: basketsKeys.all });
    },
    [notify, queryClient],
  );

  /** Puts a basket's members back in an earlier order as far as they are still members. */
  const restoreOrder = useCallback(
    async (basketId: string, before: readonly string[]) => {
      const current = await orderOf(basketId);
      const wanted = mergeOrder(before, current);
      if (!sameOrder(wanted, current)) await reorderCall({ basketId, instrumentIds: wanted });
    },
    [orderOf, reorderCall],
  );

  const transfer = useCallback<MemberActions['transfer']>(
    async (ids, toBasketId, mode, label) => {
      if (toBasketId === sourceId || ids.length === 0) return false;
      const targetName = nameOf(toBasketId);
      // What the two baskets hold before, so Undo can put exactly that back.
      let before: Snapshot | null = null;
      try {
        const [sourceRoster, targetOrder] = await Promise.all([rosterOf(sourceId), orderOf(toBasketId)]);
        before = { source: sourceRoster, target: new Set(targetOrder) };
      } catch {
        before = null;
      }
      try {
        const result = await transferCall({ fromBasketId: sourceId, toBasketId, instrumentIds: ids, mode });
        if (result.added === 0 && result.removed === 0) {
          notify(`Nothing changed: ${subject(ids.length, label)} already in ${targetName}`, 'info');
          return false;
        }
        const message = transferMessage(result, mode, targetName, label);
        const moved = before ? ids.filter((id) => before.source.order.includes(id)) : [];
        const newlyThere = before ? moved.filter((id) => !before.target.has(id)) : [];
        // Without the situation before, only a transfer that changed everything in both baskets can be undone.
        const undoable = before !== null || (result.alreadyInTarget === 0 && result.notInSource === 0);
        if (!undoable) {
          notify(`${message}. This cannot be undone from here.`, 'warning');
          return true;
        }
        const undoIds = before ? newlyThere : [...ids];
        const undo = async () => {
          try {
            if (mode === 'copy') {
              await removeCall({ basketId: toBasketId, instrumentIds: undoIds });
              notify(
                `Copy undone: removed ${subject(undoIds.length, label)} from ${targetName}${
                  result.newlyTracked > 0 ? '. Instruments that were tracked for it stay tracked' : ''
                }`,
              );
              return;
            }
            if (undoIds.length > 0) {
              await transferCall({
                fromBasketId: toBasketId,
                toBasketId: sourceId,
                instrumentIds: undoIds,
                mode: 'move',
              });
            }
            const rest = before ? moved.filter((id) => before.target.has(id)) : [];
            if (rest.length > 0) await addBack(sourceId, rest, before?.source.notes);
            if (before) await restoreOrder(sourceId, before.source.order);
            notify(`Move undone: ${subject(moved.length || ids.length, label)} back in ${nameOf(sourceId)}`);
          } catch (error) {
            fail(error, 'The change could not be undone.');
          }
        };
        notify(message, 'success', {
          label: 'Undo',
          onClick: () => {
            void undo();
          },
        });
        return true;
      } catch (error) {
        fail(error, mode === 'copy' ? 'The instruments could not be copied.' : 'The instruments could not be moved.');
        return false;
      }
    },
    [sourceId, nameOf, rosterOf, orderOf, transferCall, notify, removeCall, addBack, restoreOrder, fail],
  );

  const remove = useCallback<MemberActions['remove']>(
    async (ids, label) => {
      if (ids.length === 0) return false;
      const before = await rosterOf(sourceId).catch(() => null);
      try {
        const result = await removeCall({ basketId: sourceId, instrumentIds: ids });
        const message = removedMessage(result, nameOf(sourceId), label);
        const gone = before ? ids.filter((id) => before.order.includes(id)) : [...ids];
        notify(message, 'success', {
          label: 'Undo',
          onClick: () => {
            void (async () => {
              try {
                await addBack(sourceId, gone, before?.notes);
                if (before) await restoreOrder(sourceId, before.order);
                notify(
                  `Restored ${subject(gone.length, label)} in ${nameOf(sourceId)}${
                    before ? '' : '. Their notes could not be restored'
                  }`,
                );
              } catch (error) {
                fail(error, 'The change could not be undone.');
              }
            })();
          },
        });
        return true;
      } catch (error) {
        fail(error, 'The instruments could not be removed.');
        return false;
      }
    },
    [sourceId, nameOf, rosterOf, removeCall, addBack, restoreOrder, notify, fail],
  );

  const busy = transferring || removing || adding;
  return useMemo(() => ({ busy, transfer, remove }), [busy, transfer, remove]);
}
