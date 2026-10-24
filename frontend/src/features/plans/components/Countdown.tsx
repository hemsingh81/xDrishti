import { useEffect, useRef } from 'react';
import { useNow } from '../hooks/useNow';
import { formatCountdown } from '../lib/planText';

/**
 * Live countdown to the review cutoff; says "Cutoff passed" afterwards. `onElapsed` is called once when the clock
 * crosses the cutoff while the countdown is on screen, so the screen can read the server again (the server decides).
 */
export function Countdown({ cutoff, onElapsed }: { cutoff: string; onElapsed?: () => void }) {
  const now = useNow();
  const cutoffMs = Date.parse(cutoff);
  const passed = now >= cutoffMs;
  const wasBefore = useRef(!passed);
  useEffect(() => {
    if (!passed) {
      wasBefore.current = true;
    } else if (wasBefore.current) {
      wasBefore.current = false;
      onElapsed?.();
    }
  }, [passed, onElapsed]);
  return <span>{formatCountdown(cutoffMs, now)}</span>;
}
