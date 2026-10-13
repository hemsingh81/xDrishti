import { act, renderHook } from '@testing-library/react';
import { useWallClock } from './useWallClock';

const start = new Date('2026-10-13T10:00:00.400Z');

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(start);
});
afterEach(() => {
  vi.useRealTimers();
  Object.defineProperty(document, 'hidden', { configurable: true, value: false });
});

const hide = (hidden: boolean) => {
  Object.defineProperty(document, 'hidden', { configurable: true, value: hidden });
  document.dispatchEvent(new Event('visibilitychange'));
};

describe('useWallClock', () => {
  it('reads the time rounded down to the second and moves on every second', () => {
    const { result } = renderHook(() => useWallClock());

    expect(result.current).toBe(Date.parse('2026-10-13T10:00:00Z'));
    act(() => {
      vi.advanceTimersByTime(1000);
    });
    expect(result.current).toBe(Date.parse('2026-10-13T10:00:01Z'));
    act(() => {
      vi.advanceTimersByTime(2000);
    });
    expect(result.current).toBe(Date.parse('2026-10-13T10:00:03Z'));
  });

  it('rounds to the resolution asked for: a minute counter moves once a minute', () => {
    const renders = vi.fn();
    const { result } = renderHook(() => {
      renders();
      return useWallClock(60_000);
    });
    const first = result.current;

    act(() => {
      vi.advanceTimersByTime(30_000);
    });
    expect(result.current).toBe(first);
    expect(renders).toHaveBeenCalledTimes(1);
    act(() => {
      vi.advanceTimersByTime(30_000);
    });
    expect(result.current).toBe(first + 60_000);
  });

  it('serves every subscriber with one timer and stops it with the last one', () => {
    const a = renderHook(() => useWallClock());
    const b = renderHook(() => useWallClock(60_000));
    expect(vi.getTimerCount()).toBe(1);

    a.unmount();
    expect(vi.getTimerCount()).toBe(1);
    b.unmount();
    expect(vi.getTimerCount()).toBe(0);
  });

  it('pauses while the tab is hidden and catches up when it is shown again', () => {
    const { result } = renderHook(() => useWallClock());

    act(() => {
      hide(true);
    });
    expect(vi.getTimerCount()).toBe(0);
    act(() => {
      vi.setSystemTime(new Date('2026-10-13T10:05:00.200Z'));
      vi.advanceTimersByTime(60_000);
    });
    act(() => {
      hide(false);
    });

    expect(vi.getTimerCount()).toBe(1);
    expect(result.current).toBe(Date.parse('2026-10-13T10:06:00Z'));
  });
});
