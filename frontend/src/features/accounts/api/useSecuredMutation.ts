import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { useStepUp } from '@/features/auth';

export interface SecuredMutationOptions {
  /** The variables contain secrets (broker API key/secret): do not keep the finished mutation in the cache. */
  sensitive?: boolean;
}

/**
 * A mutation for a change that needs a fresh password check: a 403 `auth.step_up_required` opens the password dialog
 * and the request is repeated once the password is accepted (see features/auth).
 */
export function useSecuredMutation<TVariables, TData>(
  action: (variables: TVariables) => Promise<TData>,
  onSuccess: (queryClient: QueryClient, data: TData, variables: TVariables) => Promise<unknown> | undefined,
  options: SecuredMutationOptions = {},
) {
  const queryClient = useQueryClient();
  const { withStepUp } = useStepUp();
  return useMutation<TData, Error, TVariables>({
    mutationFn: withStepUp(action),
    onSuccess: (data, variables) => onSuccess(queryClient, data, variables),
    // Variables that hold secrets leave the mutation cache as soon as nobody watches the mutation any more.
    ...(options.sensitive ? { gcTime: 0 } : {}),
  });
}
