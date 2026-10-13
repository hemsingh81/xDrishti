import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { installFakeIntersectionObserver } from './intersection';
import { categoryStub, chartStub } from './chartStub';
import { releasePending } from './gate';
import { server } from './server';

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

// jsdom resolves relative fetch URLs against this origin.
beforeAll(() => {
  server.listen({ onUnhandledFrame: 'error' }); // any unmocked request fails the test
  const mocked = globalThis.fetch;
  globalThis.fetch = async (input, init) => {
    try {
      return await mocked(input, init);
    } catch (error) {
      if (refused(error)) escaped.push(input instanceof Request ? input.url : String(input));
      throw error;
    }
  };
});
afterEach(async () => {
  // Answer what a test left hanging while the screen is still there (see gate.ts), then take the screen down.
  await releasePending();
  cleanup();
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
