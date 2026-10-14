import { act } from '@testing-library/react';

let clockNow = 0;

/**
 * Sets the clock the screens read (`Date.now()`) to a moment, without faking timers: the page keeps its real timers
 * and requests, so the network mocks behave as in every other test. Undone by `vi.restoreAllMocks()`.
 */
export function setNow(iso: string) {
  clockNow = Date.parse(iso);
  vi.spyOn(Date, 'now').mockImplementation(() => clockNow);
}

/** Lets minutes pass on the clock set by `setNow`: the shared clock re-reads it as when a hidden tab is shown again. */
export function passMinutes(minutes: number) {
  clockNow += minutes * 60_000;
  act(() => {
    document.dispatchEvent(new Event('visibilitychange'));
  });
}

/**
 * Takes over the intervals of the given lengths (a query's `refetchInterval`) so a test can fire them by hand: fake timers
 * would stall the network mock. Call it once per test, with every length of interest. Undone by `vi.restoreAllMocks()`.
 */
export function captureIntervals(...delays: number[]) {
  const timers = new Map<number, { delay: number; callback: () => void }>();
  const realSet = globalThis.setInterval.bind(globalThis);
  const realClear = globalThis.clearInterval.bind(globalThis);
  let nextId = 9_000_000;
  vi.spyOn(globalThis, 'setInterval').mockImplementation(((callback: () => void, delay?: number) => {
    if (delay === undefined || !delays.includes(delay)) return realSet(callback, delay);
    nextId += 1;
    timers.set(nextId, { delay, callback });
    return nextId;
  }) as unknown as typeof setInterval);
  vi.spyOn(globalThis, 'clearInterval').mockImplementation((id?: number) => {
    if (id !== undefined && timers.delete(id)) return;
    realClear(id);
  });
  const of = (delay: number) => [...timers.values()].filter((timer) => timer.delay === delay);
  return {
    count: (delay: number) => of(delay).length,
    fire: (delay: number) => {
      act(() => {
        for (const timer of of(delay)) timer.callback();
      });
    },
  };
}

/**
 * Takes over the timeouts of the given lengths so a test can fire them by hand instead of waiting for them (the other
 * timeouts run as usual). Call it once per test, with every length of interest. Undone by `vi.restoreAllMocks()`.
 */
export function captureTimeouts(...delays: number[]) {
  const timers = new Map<number, { delay: number; callback: () => void }>();
  const realSet = globalThis.setTimeout.bind(globalThis);
  const realClear = globalThis.clearTimeout.bind(globalThis);
  let nextId = 8_000_000;
  vi.spyOn(globalThis, 'setTimeout').mockImplementation(((callback: () => void, delay?: number, ...args: unknown[]) => {
    if (delay === undefined || !delays.includes(delay)) return realSet(callback, delay, ...args);
    nextId += 1;
    timers.set(nextId, { delay, callback });
    return nextId;
  }) as unknown as typeof setTimeout);
  vi.spyOn(globalThis, 'clearTimeout').mockImplementation((id?: number) => {
    if (id !== undefined && timers.delete(id)) return;
    realClear(id);
  });
  const of = (delay: number) => [...timers.values()].filter((timer) => timer.delay === delay);
  return {
    count: (delay: number) => of(delay).length,
    /** Runs the waiting timeouts of that length now (each runs once). */
    fire: (delay: number) => {
      act(() => {
        for (const [id, timer] of [...timers]) {
          if (timer.delay !== delay) continue;
          timers.delete(id);
          timer.callback();
        }
      });
    },
  };
}
