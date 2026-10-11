import { queryOptions, useQuery, type QueryClient } from '@tanstack/react-query';
import { api, ApiError, type Problem, type SessionInfo } from '@/shared/api';

/** Why there is no session: never signed in, ended by a timeout, or signed out on purpose. */
export type AnonymousReason = 'none' | 'expired' | 'signed_out';

export type SessionState =
  { status: 'authenticated'; session: SessionInfo } | { status: 'anonymous'; reason: AnonymousReason };

export const sessionQueryKey = ['auth', 'session'] as const;

export const authenticated = (session: SessionInfo): SessionState => ({ status: 'authenticated', session });
export const anonymous = (reason: AnonymousReason): SessionState => ({ status: 'anonymous', reason });

/**
 * The current session. A 401 is a normal answer ("not signed in"), not an error: it resolves to `anonymous`,
 * so the guard and the login page read one source of truth. The CSRF token lives only in this cache entry.
 */
export const sessionQuery = queryOptions({
  queryKey: sessionQueryKey,
  queryFn: async ({ signal }): Promise<SessionState> => {
    const { data, error, response } = await api.GET('/api/auth/session', { signal });
    if (data) {
      return authenticated(data);
    }
    if (response.status === 401) {
      // The body may be absent or not JSON (e.g. replaced by a proxy): treat it as "never signed in".
      const problem = error as Problem | undefined;
      return anonymous(problem?.code === 'auth.session_expired' ? 'expired' : 'none');
    }
    throw new ApiError(response.status, error);
  },
  staleTime: 60_000,
  // Returning to a tab after a long idle time notices an ended session straight away.
  refetchOnWindowFocus: true,
});

export function useSession() {
  return useQuery(sessionQuery);
}

/** Drops everything that belonged to the ended session and records why it ended (the guard then redirects). */
export function endSession(queryClient: QueryClient, reason: AnonymousReason): void {
  queryClient.removeQueries({ predicate: (query) => query.queryKey[0] !== 'auth' });
  queryClient.setQueryData(sessionQueryKey, anonymous(reason));
}

/** The CSRF token of the current session, read from the cache (never persisted anywhere). */
export function csrfTokenOf(queryClient: QueryClient): string | undefined {
  const state = queryClient.getQueryData<SessionState>(sessionQueryKey);
  return state?.status === 'authenticated' ? state.session.csrfToken : undefined;
}

/** Records the end of the step-up window in the cached session (the only place elevation is kept). */
export function setElevatedUntil(queryClient: QueryClient, elevatedUntil: string): void {
  queryClient.setQueryData<SessionState>(sessionQueryKey, (state) =>
    state?.status === 'authenticated' ? authenticated({ ...state.session, elevatedUntil }) : state,
  );
}
