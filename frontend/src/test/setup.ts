import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { setHubTransport } from '@/shared/realtime';
import { installFakeIntersectionObserver } from './intersection';
import { categoryStub, chartStub } from './chartStub';
import { releasePending } from './gate';
import { server } from './server';
import { pinNow, unpinClock } from './pinClock';

// Flows such as "save → password dialog → retry → toast" pass through several MUI transitions; under the parallel
// full suite they can take longer than the 1 s default, so async queries (findBy*, waitFor) wait up to 5 s.
configure({ asyncUtilTimeout: 5000 });

installFakeIntersectionObserver();

// jsdom has no canvas: every test gets a stand-in for the lazy ECharts module (EChartsCanvas.test.tsx brings the real one).
vi.mock('@/shared/ui/chart/EChartsCanvas', async () => ({ default: (await import('./chartStub')).default }));
vi.mock('@/shared/ui/chart/CategoryChartCanvas', async () => ({
  default: (await import('./chartStub')).CategoryChartCanvasStub,
}));

// Requests that got past the network mock and hit the real network. `onUnhandledFrame: 'error'` only sees requests the mock
// takes in and finds no handler for; a request that the mock never took in (seen once after a hung request was aborted) goes
// straight to the network and fails there with ECONNREFUSED, which looks like "the server is down", not like a test bug.
// This wrapper notes such a request, and the test that made it fails afterwards with its URL.
const escaped: string[] = [];
const refused = (error: unknown) => {
  const cause = error instanceof Error ? (error.cause as { code?: unknown } | undefined) : undefined;
  return cause?.code === 'ECONNREFUSED';
};

// A multipart upload (the trades import, see `multipart.ts`): the network mock cannot take in a request whose body is the
// stream of a form (its copy of the body ends early and the request fails), so such a request is handed over with the same
// bytes as text; the handler reads it with `readUpload`.
const isMultipart = (input: Request | string | URL): input is Request =>
  input instanceof Request && (input.headers.get('content-type') ?? '').startsWith('multipart/form-data');

// jsdom resolves relative fetch URLs against this origin.
beforeAll(() => {
  server.listen({ onUnhandledFrame: 'error' }); // any unmocked request fails the test
  const mocked = globalThis.fetch;
  globalThis.fetch = async (input, init) => {
    try {
      if (isMultipart(input)) {
        const body = await input.clone().text();
        return await mocked(new Request(input, { body, signal: input.signal }), init);
      }
      return await mocked(input, init);
    } catch (error) {
      if (refused(error)) escaped.push(input instanceof Request ? input.url : String(input));
      throw error;
    }
  };
});
// Diagnostic (npm run test:clock): XD_TEST_NOW shifts `Date` for every test; the default is the real clock. A test that fakes
// timers itself keeps control of them, but starts them at that instant unless it gives its own `now`.
const shiftedNow = (import.meta.env as Record<string, string | undefined>).XD_TEST_NOW;
if (shiftedNow) {
  if (Number.isNaN(Date.parse(shiftedNow))) throw new Error(`XD_TEST_NOW is not an ISO instant: ${shiftedNow}`);
  const fakeTimersOf = vi.useFakeTimers.bind(vi);
  vi.useFakeTimers = (config) => fakeTimersOf({ now: new Date(shiftedNow), ...config });
}
beforeEach(() => {
  if (shiftedNow) pinNow(shiftedNow);
});
afterEach(() => {
  unpinClock(); // whatever a test pinned (pinClock, setNow) or faked is not for the next one
});

// The live hub: unless a test installs its own (see hubFake.ts), a connection never completes, which for the screens is the
// same as having no hub (they read once a minute, as before it existed).
beforeEach(() => {
  setHubTransport(() => new Promise(() => undefined));
});
afterEach(async () => {
  // Answer what a test left hanging while the screen is still there (see gate.ts), then take the screen down.
  await releasePending();
  cleanup();
  // What a test left in the browser's storage (the bell's read state, the pop-ups that were shown) is not for the next one.
  window.localStorage.clear();
  setHubTransport();
  if (escaped.length > 0) {
    throw new Error(`A request escaped the network mock and reached the real network: ${escaped.splice(0).join(', ')}`);
  }
  chartStub.reset();
  categoryStub.reset();
  server.resetHandlers();
});
afterAll(() => {
  server.close();
});
