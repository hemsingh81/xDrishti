import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api, ensureOk, unwrap, type Basket, type BasketInput } from '@/shared/api';
import { basketsKeys } from './basketsQueries';

export interface SaveBasketVariables {
  /** Absent: create. */
  id?: string;
  body: BasketInput;
}

/** Creates a basket, or replaces the attributes of one (members untouched). */
export function useSaveBasket() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: SaveBasketVariables): Promise<Basket> =>
      id === undefined
        ? unwrap(await api.POST('/api/baskets', { body }))
        : unwrap(await api.PUT('/api/baskets/{id}', { params: { path: { id } }, body })),
    onSettled: () => queryClient.invalidateQueries({ queryKey: basketsKeys.all }),
  });
}

/** Archives a basket (idempotent): it leaves the list, its history stays. */
export function useArchiveBasket() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      ensureOk(await api.DELETE('/api/baskets/{id}', { params: { path: { id } } }));
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: basketsKeys.all }),
  });
}

/**
 * Puts the baskets in this order (every manual basket once). The list shows the new order at once; when the server
 * refuses, the old order comes back and the caller tells the owner.
 */
export function useReorderBaskets() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (ids: readonly string[]) => {
      ensureOk(await api.PUT('/api/baskets/order', { body: { ids: [...ids] } }));
    },
    onMutate: async (ids) => {
      await queryClient.cancelQueries({ queryKey: basketsKeys.list });
      const previous = queryClient.getQueryData<Basket[]>(basketsKeys.list);
      if (previous) {
        const byId = new Map(previous.map((basket) => [basket.id, basket]));
        const manual = ids.flatMap((id, position) => {
          const basket = byId.get(id);
          return basket ? [{ ...basket, position }] : [];
        });
        // The order lists the owner's baskets only: the portfolio baskets stay first, as they are.
        queryClient.setQueryData(basketsKeys.list, [...previous.filter((basket) => basket.system), ...manual]);
      }
      return { previous };
    },
    onError: (_error, _ids, context) => {
      if (context?.previous) queryClient.setQueryData(basketsKeys.list, context.previous);
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: basketsKeys.list }),
  });
}
