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
