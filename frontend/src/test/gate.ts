import { act } from '@testing-library/react';
import { HttpResponse } from 'msw';

/**
 * A request that is left hanging and then aborted (the screen unmounts) can let the NEXT request to the same URL slip
 * past the network mock and reach the real network (ECONNREFUSED). So a handler that must "still be loading" while the
 * test looks must not hang for ever: it waits here, and `releasePending()` answers it (503) before the screen goes away.
 * `setup.ts` calls it after every test, so a test only has to use `pendingResponse()`; a test that unmounts and renders
 * again in the middle calls `releasePending()` itself first.
 */
const waiting = new Set<() => void>();

/** A response that does not come until `releasePending()` (at the latest when the test ends). */
export const pendingResponse = (): Promise<Response> =>
  new Promise((resolve) => {
    waiting.add(() => {
      resolve(new HttpResponse(null, { status: 503 }));
    });
  });

/** Answers every response held by `pendingResponse()` and lets the screen take the answers in. */
export async function releasePending(): Promise<void> {
  if (waiting.size === 0) return;
  await act(async () => {
    for (const release of waiting) release();
    waiting.clear();
    await new Promise((resolve) => setTimeout(resolve, 30));
  });
}
