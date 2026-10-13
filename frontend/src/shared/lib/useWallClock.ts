import { useCallback, useSyncExternalStore } from 'react';

/**
 * One shared clock for every component that shows the time: a single timer, aligned to the second, serves all
 * subscribers (the top-bar clock, countdowns, the timeline's "now"). It runs only while something is subscribed, is
 * paused while the tab is hidden and re-reads the time when the tab is shown again.
 */
const listeners = new Set<() => void>();
let timer: ReturnType<typeof setTimeout> | undefined;
let current = 0;

const secondNow = () => Math.floor(Date.now() / 1000) * 1000;

function tell() {
  for (const listener of listeners) listener();
}

function schedule() {
  // A millisecond past the boundary, so the timer never wakes a hair before the second changes.
  timer = setTimeout(
    () => {
      current = secondNow();
      tell();
      schedule();
    },
    1000 - (Date.now() % 1000) + 1,
  );
}

function stop() {
  clearTimeout(timer);
  timer = undefined;
}

function start() {
  if (timer !== undefined || document.hidden) return;
  current = secondNow();
  schedule();
}

function onVisibility() {
  stop();
  if (document.hidden) return;
  start();
  tell();
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  if (listeners.size === 1) {
    document.addEventListener('visibilitychange', onVisibility);
    start();
  }
  return () => {
    listeners.delete(listener);
    if (listeners.size === 0) {
      document.removeEventListener('visibilitychange', onVisibility);
      stop();
    }
  };
}

/** While the timer runs the cached second; otherwise (no subscriber yet, tab hidden) the time right now. */
const read = () => (timer === undefined ? secondNow() : current);

/**
 * The current time in ms (`Date.now()`), rounded down to `resolutionMs` (1000 = every second, 60 000 = every minute).
 * The component re-renders only when the rounded value changes, so a minute-resolution countdown does not render
 * every second.
 */
export function useWallClock(resolutionMs = 1000): number {
  const snapshot = useCallback(() => Math.floor(read() / resolutionMs) * resolutionMs, [resolutionMs]);
  return useSyncExternalStore(subscribe, snapshot);
}
