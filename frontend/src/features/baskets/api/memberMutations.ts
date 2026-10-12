import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import {
  api,
  ensureOk,
  queryRoots,
  unwrap,
  type AddMembersResult,
  type BasketMembers,
  type RemoveMembersResult,
  type TransferResult,
} from '@/shared/api';
import { chunk } from '@/shared/lib/chunks';
import { PartialAddError } from '../lib/errors';
import { basketsKeys } from './basketsQueries';

/**
 * Reloads what a change of members touches: the list (counts), the members and history of the baskets involved, the
 * baskets shown for instruments and, when instruments may have been tracked on the way, the Instruments screen's lists
 * (reached by the shared key root, not by importing that feature).
 */
function reloadAfterChange(queryClient: QueryClient, basketIds: readonly string[], tracking: boolean) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: basketsKeys.list }),
    queryClient.invalidateQueries({ queryKey: basketsKeys.ofInstruments }),
    ...basketIds.flatMap((id) => [
      queryClient.invalidateQueries({ queryKey: [...basketsKeys.members, id] }),
      queryClient.invalidateQueries({ queryKey: basketsKeys.historyOf(id) }),
    ]),
    ...(tracking ? [queryClient.invalidateQueries({ queryKey: queryRoots.instruments })] : []),
  ]);
}

export interface AddMembersVariables {
  basketId: string;
  instrumentIds: readonly string[];
  note?: string;
}

/**
 * Adds instruments to a basket (idempotent; instruments that are not tracked yet are tracked). More than 500 ids go out
 * in several calls, one after the other: each call is all or nothing, so when a later one fails the earlier ones stay
 * (the error is then a `PartialAddError` that carries what was applied). Every settle reloads the lists, failed or not.
 */
export function useAddMembers() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ basketId, instrumentIds, note }: AddMembersVariables): Promise<AddMembersResult> => {
      const total: AddMembersResult = { requested: 0, added: 0, alreadyMembers: 0, newlyTracked: 0, memberCount: 0 };
      for (const ids of chunk(instrumentIds)) {
        let part: AddMembersResult;
        try {
          part = unwrap(
            await api.POST('/api/baskets/{id}/members', {
              params: { path: { id: basketId } },
              body: { instrumentIds: ids, note: note ?? null },
            }),
          );
        } catch (error) {
          // Earlier calls stay applied: say so, so the caller can tell the owner what was done.
          throw total.requested > 0 ? new PartialAddError(total, error) : error;
        }
        total.requested += part.requested;
        total.added += part.added;
        total.alreadyMembers += part.alreadyMembers;
        total.newlyTracked += part.newlyTracked;
        total.memberCount = part.memberCount;
      }
      return total;
    },
    onSettled: (_data, _error, { basketId }) => reloadAfterChange(queryClient, [basketId], true),
  });
}

export interface RemoveMembersVariables {
  basketId: string;
  instrumentIds: readonly string[];
}

/** Removes instruments from a basket (idempotent; their history stays, they stay tracked). Chunked like `useAddMembers`. */
export function useRemoveMembers() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ basketId, instrumentIds }: RemoveMembersVariables): Promise<RemoveMembersResult> => {
      const total: RemoveMembersResult = { requested: 0, removed: 0, notMembers: 0, memberCount: 0 };
      for (const ids of chunk(instrumentIds)) {
        const part = unwrap(
          await api.DELETE('/api/baskets/{id}/members', {
            params: { path: { id: basketId } },
            body: { instrumentIds: ids },
          }),
        );
        total.requested += part.requested;
        total.removed += part.removed;
        total.notMembers += part.notMembers;
        total.memberCount = part.memberCount;
      }
      return total;
    },
    onSettled: (_data, _error, { basketId }) => reloadAfterChange(queryClient, [basketId], false),
  });
}

export interface TransferVariables {
  fromBasketId: string;
  toBasketId: string;
  instrumentIds: readonly string[];
  mode: 'copy' | 'move';
}

/** Copies or moves instruments from one basket to another, atomically per call of 500 (the results add up). */
export function useTransferMembers() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      fromBasketId,
      toBasketId,
      instrumentIds,
      mode,
    }: TransferVariables): Promise<TransferResult> => {
      const total: TransferResult = {
        mode,
        requested: 0,
        added: 0,
        alreadyInTarget: 0,
        removed: 0,
        notInSource: 0,
        newlyTracked: 0,
        sourceMemberCount: 0,
        targetMemberCount: 0,
      };
      for (const ids of chunk(instrumentIds)) {
        const part = unwrap(
          await api.POST('/api/baskets/transfer', { body: { fromBasketId, toBasketId, instrumentIds: ids, mode } }),
        );
        total.requested += part.requested;
        total.added += part.added;
        total.alreadyInTarget += part.alreadyInTarget;
        total.removed += part.removed;
        total.notInSource += part.notInSource;
        total.newlyTracked += part.newlyTracked;
        total.sourceMemberCount = part.sourceMemberCount;
        total.targetMemberCount = part.targetMemberCount;
      }
      return total;
    },
    onSettled: (_data, _error, { fromBasketId, toBasketId }) =>
      reloadAfterChange(queryClient, [fromBasketId, toBasketId], true),
  });
}

export interface ReorderMembersVariables {
  basketId: string;
  /** Every current member once, in the wanted order. */
  instrumentIds: readonly string[];
}

/** The members in the wanted order. */
export function reorderedMembers(members: BasketMembers, instrumentIds: readonly string[]): BasketMembers {
  const byInstrument = new Map(members.items.map((item) => [item.instrument.id, item]));
  const items = instrumentIds.flatMap((id, position) => {
    const item = byInstrument.get(id);
    return item ? [{ ...item, position }] : [];
  });
  return { ...members, items };
}

/**
 * Puts the members in this order. The grid shows the new order at once; when the server refuses (a conflict, a changed
 * basket) the old order comes back and the caller tells the owner. Either way the list is read again afterwards.
 */
export function useReorderMembers() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ basketId, instrumentIds }: ReorderMembersVariables) => {
      ensureOk(
        await api.PUT('/api/baskets/{id}/members/order', {
          params: { path: { id: basketId } },
          body: { instrumentIds: [...instrumentIds] },
        }),
      );
    },
    onMutate: async ({ basketId, instrumentIds }) => {
      const key = basketsKeys.membersOf(basketId, null);
      await queryClient.cancelQueries({ queryKey: key });
      const previous = queryClient.getQueryData<BasketMembers>(key);
      if (previous) queryClient.setQueryData(key, reorderedMembers(previous, instrumentIds));
      return { previous, key };
    },
    onError: (_error, _variables, context) => {
      if (context?.previous) queryClient.setQueryData(context.key, context.previous);
    },
    onSettled: (_data, _error, { basketId }) =>
      queryClient.invalidateQueries({ queryKey: basketsKeys.membersOf(basketId, null) }),
  });
}
