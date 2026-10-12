import { useEffect, useEffectEvent } from 'react';

/** The longest delay a timer takes (about 24.8 days); a later moment is not scheduled. */
const maxDelayMs = 2 ** 31 - 1;

/**
 * Calls `refetch` once, `graceMs` after the moment `at` (an ISO string), for example just after a scheduled run starts.
 * Nothing is scheduled for a moment that has passed or is not known; when `at` changes the timer is set again.
 */
export function useRefetchAt(at: string | null | undefined, refetch: () => void, graceMs = 5000) {
  const fire = useEffectEvent(refetch);
  useEffect(() => {
    if (!at) return;
    const delay = Date.parse(at) + graceMs - Date.now();
    if (!(delay > 0) || delay > maxDelayMs) return;
    const timer = setTimeout(() => {
      fire();
    }, delay);
    return () => {
      clearTimeout(timer);
    };
  }, [at, graceMs]);
}
