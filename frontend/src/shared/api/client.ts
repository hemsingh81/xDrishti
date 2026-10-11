import createClient from 'openapi-fetch';
import type { paths } from './schema';
import type { Problem, ValidationProblem } from './types';

/**
 * Typed HTTP client generated from the backend OpenAPI contract (npm run gen:api).
 * Same-origin requests: xd-proxy (production) or the Vite proxy (development) forwards /api to the API.
 */
export const api = createClient<paths>({
  baseUrl: globalThis.location.origin,
  // The session cookie (xd.sid) travels on same-origin requests only.
  credentials: 'same-origin',
  // Resolve fetch per call (not at import time) so interceptors such as MSW in tests are honoured.
  fetch: (request) => globalThis.fetch(request),
});

/**
 * Hooks the auth feature plugs into the shared client (shared/ never imports features):
 * - getCsrfToken: current CSRF token (kept in memory by the session query), sent on every unsafe request;
 * - onUnauthorized: called once per 401 response from any call except login and the session probe.
 */
export interface ApiAuthHooks {
  getCsrfToken: () => string | undefined;
  onUnauthorized: (problem: Problem | undefined) => void;
}

let authHooks: ApiAuthHooks | undefined;

/** Registers (or replaces) the auth hooks. Idempotent. */
export function configureApiAuth(hooks: ApiAuthHooks | undefined): void {
  authHooks = hooks;
}

const safeMethods = new Set(['GET', 'HEAD', 'OPTIONS']);
const anonymousPaths = new Set<string>(['/api/auth/login', '/api/auth/session']);
const stepUpPath = '/api/auth/step-up';

api.use({
  onRequest({ request, schemaPath }) {
    if (safeMethods.has(request.method) || schemaPath === '/api/auth/login') {
      return undefined;
    }
    const token = authHooks?.getCsrfToken();
    if (token) {
      request.headers.set('X-CSRF-Token', token);
    }
    return request;
  },
  async onResponse({ response, schemaPath }) {
    if (response.status !== 401 || anonymousPaths.has(schemaPath) || !authHooks) {
      return undefined;
    }
    const problem = (await response
      .clone()
      .json()
      .catch(() => undefined)) as Problem | undefined;
    // A wrong password in the step-up dialog is an answer to the user, not an ended session.
    if (schemaPath === stepUpPath && problem?.code === 'auth.invalid_credentials') {
      return undefined;
    }
    authHooks.onUnauthorized(problem);
    return undefined;
  },
});

/** Thrown for non-2xx responses; carries RFC 7807 problem details when the API sends them. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem?: ValidationProblem,
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
  throw new ApiError(result.response.status, result.error as ValidationProblem | undefined);
}

/** For calls without a response body (204): returns normally on success, throws ApiError otherwise. */
export function ensureOk(result: { error?: unknown; response: Response }): void {
  if (!result.response.ok) {
    throw new ApiError(result.response.status, result.error as ValidationProblem | undefined);
  }
}
