import { queryOptions } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';
import { actionStatuses, type ActionStatus } from '../lib/actions';
import { portfolioKeys } from './portfolioQueries';

export interface ActionScope {
  account: string | null;
  bucket: string | null;
}

/** The API parameters of the Action center: the scope bar's account and bucket and the status filter (`open` is the default). */
export const actionParams = (scope: ActionScope, status: ActionStatus) => ({
  accountId: scope.account ?? undefined,
  bucketId: scope.bucket ?? undefined,
  status: status === 'open' ? undefined : status,
});

/** Whether two cached lists were read for the same account and bucket (only the status may differ). */
function sameScope(a: readonly unknown[] | undefined, b: readonly unknown[]): boolean {
  const scopeOf = (key: readonly unknown[] | undefined) => {
    const query: unknown = key?.[3];
    if (typeof query !== 'object' || query === null) return null;
    const { accountId, bucketId } = query as { accountId?: unknown; bucketId?: unknown };
    return `${String(accountId)}|${String(bucketId)}`;
  };
  return a !== undefined && scopeOf(a) === scopeOf(b);
}

/**
 * The items that need a decision (or are snoozed or done) in the scope, with the counts of the whole scope. A refetch of
 * the same list keeps its items. Another status of the same scope shows the last answer's counts as a placeholder (the
 * counts do not depend on the status) but never its items; another scope shows nothing until its own answer comes.
 */
export const actionsQuery = (scope: ActionScope, status: ActionStatus) => {
  const query = actionParams(scope, status);
  return queryOptions({
    queryKey: [...portfolioKeys.actions, 'list', query] as const,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/portfolio/actions', { params: { query }, signal })),
    placeholderData: (previous, previousQuery) =>
      sameScope(previousQuery?.queryKey, [...portfolioKeys.actions, 'list', query]) ? previous : undefined,
  });
};

/** The status a cached list was read for (the last part of its key); a key of another shape counts as the default. */
export function statusOfKey(key: readonly unknown[]): ActionStatus {
  const query: unknown = key[3];
  const status = typeof query === 'object' && query !== null && 'status' in query ? query.status : undefined;
  return actionStatuses.find((s) => s === status) ?? 'open';
}
