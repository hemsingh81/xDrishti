import type { QueryClient } from '@tanstack/react-query';
import { configureApiAuth } from '@/shared/api';
import { csrfTokenOf, endSession } from '../api/sessionQuery';

/**
 * Wires the shared API client to the session cache: attaches the CSRF token to unsafe requests and turns any
 * 401 into "session ended" (cache cleared; the route guard then redirects once to /login?next=…).
 * Call once per QueryClient, before the first request.
 */
export function connectAuth(queryClient: QueryClient): void {
  configureApiAuth({
    getCsrfToken: () => csrfTokenOf(queryClient),
    onUnauthorized: (problem) => {
      endSession(queryClient, problem?.code === 'auth.session_expired' ? 'expired' : 'none');
    },
  });
}
