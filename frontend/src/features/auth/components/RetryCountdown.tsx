import { formatCountdown, useCountdown } from '../hooks/useCountdown';

/** Live m:ss countdown; calls onElapsed at zero. Not announced every second (aria-live off). */
export function RetryCountdown({ seconds, onElapsed }: { seconds: number; onElapsed: () => void }) {
  const remaining = useCountdown(seconds, onElapsed);
  return (
    <strong aria-live="off" style={{ fontVariantNumeric: 'tabular-nums' }}>
      {formatCountdown(remaining)}
    </strong>
  );
}
