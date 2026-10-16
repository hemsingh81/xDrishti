import { act, renderHook } from '@testing-library/react';
import { rereadGapMs, useRereadOnMismatch, type Rereadable } from './useRereadOnMismatch';

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date('2026-10-14T05:00:00Z'));
});
afterEach(() => {
  vi.useRealTimers();
});

const read = (updatedAt: number) => {
  const reread = vi.fn<() => void>();
  return { updatedAt, reread } satisfies Rereadable;
};

describe('useRereadOnMismatch', () => {
  it('does nothing while the reads fit each other', () => {
    const a = read(1);
    const b = read(2);
    renderHook(() => {
      useRereadOnMismatch(false, [a, b]);
    });
    vi.advanceTimersByTime(10 * rereadGapMs);

    expect(a.reread).not.toHaveBeenCalled();
    expect(b.reread).not.toHaveBeenCalled();
  });

  it('reads the older of the two again at once when they do not fit', () => {
    const older = read(1);
    const newer = read(2);
    renderHook(() => {
      useRereadOnMismatch(true, [newer, older]);
    });

    expect(older.reread).toHaveBeenCalledTimes(1);
    expect(newer.reread).not.toHaveBeenCalled();
  });

  it('reads again at most once per 30 s while the mismatch lasts, and stops when it ends', () => {
    const older = read(1);
    const newer = read(2);
    const { rerender } = renderHook(
      ({ mismatch, reads }: { mismatch: boolean; reads: Rereadable[] }) => {
        useRereadOnMismatch(mismatch, reads);
      },
      { initialProps: { mismatch: true, reads: [older, newer] } },
    );
    expect(older.reread).toHaveBeenCalledTimes(1);

    // The read came back, still not fitting (a newer time on the same side): not again before the gap has passed.
    vi.advanceTimersByTime(rereadGapMs - 1);
    act(() => {
      rerender({ mismatch: true, reads: [read(3), newer] });
    });
    expect(older.reread).toHaveBeenCalledTimes(1);

    const second = read(3);
    act(() => {
      rerender({ mismatch: true, reads: [second, read(4)] });
    });
    expect(second.reread).not.toHaveBeenCalled();
    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(second.reread).toHaveBeenCalledTimes(1);

    // It fits now: nothing more, however long it takes.
    act(() => {
      rerender({ mismatch: false, reads: [second, read(5)] });
    });
    vi.advanceTimersByTime(10 * rereadGapMs);
    expect(second.reread).toHaveBeenCalledTimes(1);
  });
});
