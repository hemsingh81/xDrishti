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

/**
 * A handler that must answer normally, but only when the test says so (and in the order it says): the handler awaits
 * `holdStep()`, the test calls `releaseStep()` for the oldest one. `releasePending()` releases what is left at the end.
 */
const steps: (() => void)[] = [];

export const holdStep = (): Promise<void> =>
  new Promise((resolve) => {
    steps.push(resolve);
  });

/** Lets the oldest held handler answer and the screen take the answer in. */
export async function releaseStep(): Promise<void> {
  await act(async () => {
    steps.shift()?.();
    await new Promise((resolve) => setTimeout(resolve, 30));
  });
}

/** Answers every response held by `pendingResponse()` and `holdStep()` and lets the screen take the answers in. */
export async function releasePending(): Promise<void> {
  if (waiting.size === 0 && steps.length === 0) return;
  await act(async () => {
    for (const release of waiting) release();
    waiting.clear();
    for (const release of steps.splice(0)) release();
    await new Promise((resolve) => setTimeout(resolve, 30));
  });
}
