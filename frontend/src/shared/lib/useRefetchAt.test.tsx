import { renderHook } from '@testing-library/react';
import { useRefetchAt } from './useRefetchAt';

const now = new Date('2026-10-13T10:00:00Z');

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(now);
});
afterEach(() => {
  vi.useRealTimers();
});

describe('useRefetchAt', () => {
  it('calls once, a few seconds after the moment', () => {
    const refetch = vi.fn();
    renderHook(() => {
      useRefetchAt('2026-10-13T10:00:30Z', refetch);
    });

    vi.advanceTimersByTime(34_000);
    expect(refetch).not.toHaveBeenCalled();
    vi.advanceTimersByTime(2_000);
    expect(refetch).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(600_000);
    expect(refetch).toHaveBeenCalledTimes(1);
  });

  it('schedules nothing for a moment that has passed, is not known or is too far away', () => {
    const refetch = vi.fn();
    renderHook(() => {
      useRefetchAt('2026-10-13T09:00:00Z', refetch);
      useRefetchAt(null, refetch);
      useRefetchAt(undefined, refetch);
      useRefetchAt('2027-10-13T10:00:00Z', refetch);
    });

    vi.advanceTimersByTime(86_400_000);
    expect(refetch).not.toHaveBeenCalled();
  });

  it('sets the timer again when the moment changes, and clears it when the page goes away', () => {
    const refetch = vi.fn();
    const { rerender, unmount } = renderHook(
      ({ at }) => {
        useRefetchAt(at, refetch);
      },
      { initialProps: { at: '2026-10-13T10:00:30Z' } },
    );

    rerender({ at: '2026-10-13T10:10:00Z' });
    vi.advanceTimersByTime(60_000);
    expect(refetch).not.toHaveBeenCalled();
    vi.advanceTimersByTime(600_000);
    expect(refetch).toHaveBeenCalledTimes(1);

    rerender({ at: '2026-10-13T10:30:00Z' });
    unmount();
    vi.advanceTimersByTime(3_600_000);
    expect(refetch).toHaveBeenCalledTimes(1);
  });
});
