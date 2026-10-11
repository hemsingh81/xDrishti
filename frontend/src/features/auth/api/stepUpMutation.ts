import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api';
import { setElevatedUntil } from './sessionQuery';

/** POST /api/auth/step-up: verifies the password; on success the session is elevated for a few minutes. */
export function useStepUpMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (password: string) => unwrap(await api.POST('/api/auth/step-up', { body: { password } })),
    onSuccess: ({ elevatedUntil }) => {
      setElevatedUntil(queryClient, elevatedUntil);
    },
  });
}
