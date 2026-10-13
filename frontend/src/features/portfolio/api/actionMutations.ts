import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap, type ActionList } from '@/shared/api';
import { istDayIso } from '@/shared/lib/format';
import { applyDecision, type Decision } from '../lib/actionDecision';
import { statusOfKey } from './actionQueries';
import { invalidateActionCenter } from './portfolioMutations';
import { portfolioKeys } from './portfolioQueries';

export interface DecisionVariables {
  id: string;
  decision: Decision;
}

const send = async ({ id, decision }: DecisionVariables) => {
  const params = { params: { path: { id } } };
  switch (decision) {
    case 'snooze':
      return unwrap(await api.POST('/api/portfolio/actions/{id}/snooze', params));
    case 'done':
      return unwrap(await api.POST('/api/portfolio/actions/{id}/done', params));
    case 'reopen':
      return unwrap(await api.POST('/api/portfolio/actions/{id}/reopen', params));
  }
};

/**
 * Snooze (7 days), Done or Reopen an item. The lists show the new state at once; when the server refuses (the item is not
 * produced by a rule any more, a conflict, an outage) they go back to what they were and the caller tells the owner. Either
 * way the lists are read again afterwards (once, after the last of several quick decisions).
 */
const decisionKey = ['portfolio', 'actions', 'decision'] as const;

export function useActionDecision() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: decisionKey,
    mutationFn: send,
    onMutate: async ({ id, decision }) => {
      await queryClient.cancelQueries({ queryKey: portfolioKeys.actions });
      const previous = queryClient.getQueriesData<ActionList>({ queryKey: portfolioKeys.actions });
      const today = istDayIso(Date.now());
      for (const [key, list] of previous) {
        if (list) queryClient.setQueryData<ActionList>(key, applyDecision(list, statusOfKey(key), id, decision, today));
      }
      return { previous };
    },
    onError: (_error, _variables, context) => {
      for (const [key, list] of context?.previous ?? []) queryClient.setQueryData(key, list);
    },
    // Not awaited: the answer resolves the mutation (toast, focus) and the reload follows. Only the last of several quick
    // decisions reloads, so an earlier answer cannot overwrite the later ones' optimistic state with a stale list.
    onSettled: () => {
      if (queryClient.isMutating({ mutationKey: decisionKey }) <= 1) void invalidateActionCenter(queryClient);
    },
  });
}
