import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api, ApiError } from '@/shared/api';
import { authenticated, endSession, sessionQueryKey } from './sessionQuery';

export interface Credentials {
  email: string;
  password: string;
}

export function useLogin() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (credentials: Credentials) => {
      const { data, error, response } = await api.POST('/api/auth/login', { body: credentials });
      if (!data) {
        throw new ApiError(response.status, error);
      }
      return data;
    },
    onSuccess: (session) => {
      queryClient.setQueryData(sessionQueryKey, authenticated(session));
    },
  });
}

export function useLogout() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const { response } = await api.POST('/api/auth/logout');
      // 204 on success; 401 means the session is already gone — the user is signed out either way.
      if (!response.ok && response.status !== 401) {
        throw new ApiError(response.status);
      }
    },
    onSuccess: () => {
      endSession(queryClient, 'signed_out');
    },
  });
}
