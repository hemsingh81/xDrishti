import { useEffect, useEffectEvent, useState } from 'react';

/**
 * Counts down `seconds` from mount (against the real clock, so a throttled background tab catches up),
 * ticking once a second, and calls `onElapsed` once at zero. Remount (change the `key`) to restart.
 */
export function useCountdown(seconds: number, onElapsed: () => void): number {
  const [deadline] = useState(() => Date.now() + seconds * 1000);
  const [remaining, setRemaining] = useState(seconds);
  const notifyElapsed = useEffectEvent(onElapsed);

  useEffect(() => {
    const timer = setInterval(() => {
      const left = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
      setRemaining(left);
      if (left === 0) {
        clearInterval(timer);
        notifyElapsed();
      }
    }, 1000);
    return () => {
      clearInterval(timer);
    };
  }, [deadline]);

  return remaining;
}

/** 905 -> "15:05". */
export function formatCountdown(totalSeconds: number): string {
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}
