import { api, ApiError } from './client';
import type { ValidationProblem } from './types';

const guid = '[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}';

/**
 * The command targets the API names in its notices (ADR 0027), as whole-path patterns: only these are ever sent. A new kind of
 * command needs a line here. The patterns allow letters, digits and hyphens in the ids only, so encoded dots or slashes
 * (`%2e`, `%2f`), backslashes, dot-segments, double slashes, queries and fragments cannot match.
 */
const allowedCommands: readonly RegExp[] = [
  new RegExp(`^/api/accounts/${guid}/sync/broker-history$`),
  new RegExp(`^/api/accounts/${guid}/holdings/sync$`),
  /^\/api\/services\/[A-Za-z0-9][A-Za-z0-9-]{0,63}\/run$/,
];

const targetPattern = /^POST (\/\S*)$/;

export interface Command {
  method: 'POST';
  path: string;
}

/**
 * Reads a command target of the API ("POST /api/path", ADR 0027) into its parts. Only a POST to one of the known endpoints of
 * this app's own API is accepted (no other origin, no query, no dot-segments, no encoded separators): anything else is `null`
 * and is never sent.
 */
export function parseCommand(target: string): Command | null {
  const path = targetPattern.exec(target.trim())?.[1];
  if (!path || !allowedCommands.some((pattern) => pattern.test(path))) return null;
  return { method: 'POST', path };
}

type DynamicRequest = (method: string, url: string) => Promise<{ error?: unknown; response: Response }>;

/**
 * Calls an endpoint the API named as a command target ("POST /api/accounts/…/sync/broker-history"), with the session and the
 * CSRF token like every other call (the shared client adds them). Resolves when the API accepted it; throws `ApiError` (with the
 * problem details) when it refused, and a plain `Error` for a target that is not a command. Starts nothing the app could not
 * start by itself: the targets are existing endpoints.
 */
export async function runCommand(target: string): Promise<void> {
  const command = parseCommand(target);
  if (!command) throw new Error('This action is not available.');
  // The typed client only knows literal paths; the target is data of the notice, so the call goes through the untyped door.
  const result = await (api.request as unknown as DynamicRequest)(command.method, command.path);
  if (!result.response.ok) {
    throw new ApiError(result.response.status, result.error as ValidationProblem | undefined);
  }
}
