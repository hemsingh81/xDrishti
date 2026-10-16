import { useEffect, useRef } from 'react';

/** At most one re-read per this long: the totals are made of heavy reads, and a mismatch can last (a holding changed meanwhile). */
export const rereadGapMs = 30_000;

/** A read that can be asked for again, and when it was last read (ms). */
export interface Rereadable {
  updatedAt: number;
  reread: () => void;
}

/**
 * The live totals and groups are worked out from two reads (the totals or groups, and the holdings of the scope); when they
 * do not fit each other (they were read at different moments and a holding opened or closed between) the figures stay as read.
 * While that lasts this reads the older of the two again - once, then at most once per 30 s - instead of waiting for the
 * 5-minute timer. `mismatch` false: nothing to do.
 */
export function useRereadOnMismatch(mismatch: boolean, reads: readonly Rereadable[]): void {
  const last = useRef(Number.NEGATIVE_INFINITY);
  const latest = useRef(reads);
  useEffect(() => {
    latest.current = reads;
  });
  const oldest = reads.length === 0 ? 0 : Math.min(...reads.map((read) => read.updatedAt));
  const key = reads.length;

  useEffect(() => {
    if (!mismatch || key === 0) return;
    const run = () => {
      last.current = Date.now();
      const stale = latest.current.reduce((a, b) => (b.updatedAt < a.updatedAt ? b : a));
      stale.reread();
    };
    const wait = last.current + rereadGapMs - Date.now();
    if (wait <= 0) {
      run();
      return;
    }
    const timer = setTimeout(run, wait);
    return () => {
      clearTimeout(timer);
    };
  }, [mismatch, key, oldest]);
}
