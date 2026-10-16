import { attentionCommands } from './attentionCommands.generated';
import { api, ApiError } from './client';
import type { ValidationProblem } from './types';

/**
 * The command targets the API names in its notices (ADR 0027), as whole-path patterns: only these are ever sent. The list is
 * generated from the server's own (`attentionCommands.generated.ts`, ADR 0030), so a new command on the server is allowed here
 * by regenerating, and nothing else is. The patterns allow letters, digits and hyphens in the ids only, so encoded dots or
 * slashes (`%2e`, `%2f`), backslashes, dot-segments, double slashes, queries and fragments cannot match.
 */
const targetPattern = /^([A-Z]+) (\/\S*)$/;

export interface Command {
  method: string;
  path: string;
}

/**
 * Builds the reader of command targets for a list of allowed commands: a target is accepted only when its method is the
 * method of an entry and its path matches that same entry's pattern, so a command with any method works as soon as the
 * list names it, and a method the list does not name for that path is refused.
 */
export function createCommandParser(
  commands: readonly { readonly method: string; readonly pattern: string }[],
): (target: string) => Command | null {
  const allowed = commands.map((command) => ({ method: command.method, pattern: new RegExp(command.pattern) }));
  return (target) => {
    const match = targetPattern.exec(target.trim());
    const method = match?.[1];
    const path = match?.[2];
    if (!method || !path) return null;
    return allowed.some((entry) => entry.method === method && entry.pattern.test(path)) ? { method, path } : null;
  };
}

/**
 * Reads a command target of the API ("POST /api/path", ADR 0027) into its parts. Only a command of the generated list is
 * accepted, with its own method (no other origin, no query, no dot-segments, no encoded separators): anything else is `null`
 * and is never sent.
 */
export const parseCommand = createCommandParser(attentionCommands);

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
