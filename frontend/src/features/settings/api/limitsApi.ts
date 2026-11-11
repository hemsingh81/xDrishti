import { queryOptions } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';
import type { components } from '@/shared/api/schema';

type SizingBody = components['schemas']['SizingSettingsChangeBody'];
type BasketSettingsBody = components['schemas']['BasketSettingsChangeBody'];

/** The owner's limit settings (BK-10 sizing, BK-08 baskets, BK-10b banner): every read below the one root so a change reloads them all. */
export const limitsKeys = { all: ['settings', 'limits'] as const };

export const sizingSettingsQuery = queryOptions({
  queryKey: [...limitsKeys.all, 'sizing'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/selection/sizing-settings', { signal })),
});

export const basketBudgetsQuery = queryOptions({
  queryKey: [...limitsKeys.all, 'budgets'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/selection/basket-risk-budgets', { signal })),
});

export const basketSettingsQuery = queryOptions({
  queryKey: [...limitsKeys.all, 'basket-settings'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/selection/basket-settings', { signal })),
});

/** The names behind the budgets and the gate's breadth source. */
export const limitBasketsQuery = queryOptions({
  queryKey: [...limitsKeys.all, 'baskets'] as const,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/baskets', { signal })),
});

// The writes take the password typed for THIS change; the caller clears it. Each answer is the new view.
export const changeSizing = async (body: SizingBody) =>
  unwrap(await api.PUT('/api/selection/sizing-settings', { body }));

export const changeBudget = async (basketId: string, budgetPct: number | null, password: string) =>
  unwrap(
    await api.PUT('/api/selection/basket-risk-budgets/{basketId}', {
      params: { path: { basketId } },
      body: { budgetPct, password },
    }),
  );

export const changeBasketSettings = async (body: BasketSettingsBody) =>
  unwrap(await api.PUT('/api/selection/basket-settings', { body }));

export const changeLooseningSetting = async (autoDismiss: boolean, expectedVersion: number, password: string) =>
  unwrap(await api.PUT('/api/plans/loosening-settings', { body: { autoDismiss, expectedVersion, password } }));
