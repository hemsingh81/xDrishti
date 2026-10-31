import { server } from '@/test/server';

/** The writes the Execution screens may make (spec P12-10 criterion 15), plus the two of P12-11 that already existed. */
export const allowedWrites: readonly (readonly [string, RegExp])[] = [
  ['POST', /^\/api\/execution\/modes\/[^/]+\/promotion-requests$/],
  ['POST', /^\/api\/execution\/modes\/[^/]+\/promotion-requests\/[^/]+\/confirm$/],
  ['POST', /^\/api\/execution\/modes\/[^/]+\/demotions$/],
  ['POST', /^\/api\/execution\/confirm-tickets\/[^/]+\/(confirm|skip)$/],
  ['POST', /^\/api\/execution\/day-unlocks$/],
  ['PUT', /^\/api\/execution\/limits\/[^/]+$/],
  ['POST', /^\/api\/execution\/halts$/],
  ['POST', /^\/api\/execution\/halts\/[^/]+\/release$/],
  ['POST', /^\/api\/execution\/readiness\/checks$/],
  ['POST', /^\/api\/execution\/ip-attestations$/],
  // The app's own step-up (password re-entry), which exit-all needs.
  ['POST', /^\/api\/auth\/step-up$/],
];

export const isAllowedWrite = (method: string, pathname: string): boolean =>
  method === 'GET' || allowedWrites.some(([m, re]) => m === method && re.test(pathname));

/** Registers hooks: after each test, any request that is not a GET and not on the allow-list fails the test. */
export function recordWrites() {
  const refused: string[] = [];
  const listener = ({ request }: { request: Request }) => {
    const { pathname } = new URL(request.url);
    if (!isAllowedWrite(request.method, pathname)) refused.push(`${request.method} ${pathname}`);
  };
  beforeEach(() => {
    refused.length = 0;
    server.events.on('request:start', listener);
  });
  afterEach(() => {
    server.events.removeListener('request:start', listener);
    expect(refused, 'writes outside the allow-list').toEqual([]);
  });
}
