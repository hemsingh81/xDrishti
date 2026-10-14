import { useEffect, useRef } from 'react';

/**
 * Calls `onExpire` once `durationMs` of unpaused time have passed since `restartKey` last changed. While `paused` the
 * clock stands still and carries on with what is left, so hovering or focusing a toast never shortens it.
 * `durationMs` null means no time limit.
 */
export function useToastTimer({
  durationMs,
  paused,
  restartKey,
  onExpire,
}: {
  durationMs: number | null;
  paused: boolean;
  restartKey: number;
  onExpire: () => void;
}): void {
  const latest = useRef(onExpire);
  const clock = useRef({ key: restartKey, left: durationMs ?? 0 });

  useEffect(() => {
    latest.current = onExpire;
  });

  useEffect(() => {
    if (clock.current.key !== restartKey) clock.current = { key: restartKey, left: durationMs ?? 0 };
    if (durationMs === null || paused) return undefined;
    const startedAt = Date.now();
    const timer = setTimeout(() => {
      latest.current();
    }, clock.current.left);
    return () => {
      clearTimeout(timer);
      clock.current.left -= Date.now() - startedAt;
    };
  }, [durationMs, paused, restartKey]);
}
