import './shim';
import type { Page } from '@playwright/test';
import { getResponse, RequestHandler } from 'msw';
import { server } from '../src/test/server';
import type { State } from './checks/types';

export interface ApiLog {
  /** Requests no handler answered: each is a finding (the page got a 404). */
  unanswered: string[];
  /** Requests that are not GET: a read-only screen must make none. */
  writes: string[];
  count: number;
}

const problem = (status: number, title: string) =>
  ({
    status,
    contentType: 'application/problem+json',
    body: JSON.stringify({ title, status, code: 'fixture.state' }),
  }) as const;

/**
 * Answers every /api/ call of the page from the repository's MSW handlers (src/test/server.ts plus the screen's own
 * `mount`), with no service worker and nothing leaving the machine. `loading` holds the data calls, `error` fails them.
 * The session is always answered, so the shell renders. SignalR is aborted: the screens degrade without the hub.
 */
export async function installApi(
  page: Page,
  state: State,
  mount: () => void,
): Promise<{ log: ApiLog; release: () => void }> {
  server.resetHandlers();
  mount();
  const log: ApiLog = { unanswered: [], writes: [], count: 0 };
  let release: () => void = () => undefined;
  const held = new Promise<void>((resolve) => {
    release = resolve;
  });
  await page.route(/\/hubs\//, (route) => route.abort());
  await page.route(/\/api\//, async (route) => {
    const req = route.request();
    const url = new URL(req.url());
    log.count += 1;
    if (req.method() !== 'GET') log.writes.push(`${req.method()} ${url.pathname}`);
    const isSession = url.pathname === '/api/auth/session';
    if (!isSession && state === 'loading') {
      await held;
      await route.abort().catch(() => undefined);
      return;
    }
    if (!isSession && state === 'error') {
      await route.fulfill(problem(500, 'The service is not answering (fixture).'));
      return;
    }
    const body = req.postDataBuffer();
    const request = new Request(`http://localhost${url.pathname}${url.search}`, {
      method: req.method(),
      headers: req.headers(),
      ...(body && req.method() !== 'GET' ? { body: new Uint8Array(body) } : {}),
    });
    const response = await getResponse(
      server.listHandlers().filter((h) => h instanceof RequestHandler),
      request,
    );
    if (!response) {
      log.unanswered.push(`${req.method()} ${url.pathname}`);
      await route.fulfill(problem(404, 'No fixture for this request.'));
      return;
    }
    await route.fulfill({
      status: response.status,
      headers: Object.fromEntries(response.headers),
      body: Buffer.from(await response.arrayBuffer()),
    });
  });
  return { log, release };
}
