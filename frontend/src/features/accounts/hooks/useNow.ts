import { useEffect, useState } from 'react';
import { clockTickMs } from '../lib/connection';

/**
 * The current time in ms, re-read every ~30 s while `ticking` (so countdowns move and an expired token flips to
 * "Expired" without a refetch). A new `resetKey` (e.g. a new expiry after connecting) re-reads the clock straight away.
 */
export function useNow(ticking: boolean, resetKey: string | null): number {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    if (!ticking) {
      return undefined;
    }
    const tick = () => {
      setNow(Date.now());
    };
    const first = setTimeout(tick, 0);
    const timer = setInterval(tick, clockTickMs);
    return () => {
      clearTimeout(first);
      clearInterval(timer);
    };
  }, [ticking, resetKey]);

  return now;
}
