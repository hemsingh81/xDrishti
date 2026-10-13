import { useEffect, useEffectEvent, useState } from 'react';

/**
 * `value` as it stood `delayMs` after it last changed. A change is a change of content (compared as JSON), not of
 * identity, so a form object rebuilt on every render does not restart the wait. The first value is returned at once.
 */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [settled, setSettled] = useState(value);
  const key = JSON.stringify(value);
  const settle = useEffectEvent(() => {
    setSettled(value);
  });
  useEffect(() => {
    const timer = setTimeout(settle, delayMs);
    return () => {
      clearTimeout(timer);
    };
  }, [key, delayMs]);
  return settled;
}
