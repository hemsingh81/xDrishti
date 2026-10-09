import createClient from 'openapi-fetch';
import type { paths } from './schema';

/**
 * Typed HTTP client generated from the backend OpenAPI contract (npm run gen:api).
 * Same-origin requests: xd-proxy (production) or the Vite proxy (development) forwards /api to the API.
 */
export const api = createClient<paths>({
  baseUrl: globalThis.location.origin,
  // Resolve fetch per call (not at import time) so interceptors such as MSW in tests are honoured.
  fetch: (request) => globalThis.fetch(request),
});

/** Thrown for non-2xx responses; carries RFC 7807 problem details when the API sends them. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem?: { title?: string | null; detail?: string | null },
  ) {
    super(problem?.title ?? `Request failed with status ${status}`);
    this.name = 'ApiError';
  }
}

/** Unwraps an openapi-fetch result: returns data or throws ApiError (for TanStack Query). */
export function unwrap<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (result.data !== undefined) {
    return result.data;
  }
  throw new ApiError(result.response.status, result.error as ApiError['problem']);
}
