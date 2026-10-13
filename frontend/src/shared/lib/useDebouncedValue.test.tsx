import { act, renderHook } from '@testing-library/react';
import { useDebouncedValue } from './useDebouncedValue';

describe('useDebouncedValue', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('returns the first value at once and a changed one only after the delay', () => {
    const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 300), {
      initialProps: { value: 'a' },
    });
    expect(result.current).toBe('a');

    rerender({ value: 'b' });
    act(() => {
      vi.advanceTimersByTime(299);
    });
    expect(result.current).toBe('a');
    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(result.current).toBe('b');
  });

  it('waits again while the value keeps changing, and ignores an object that is the same by content', () => {
    const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 300), {
      initialProps: { value: { n: 1 } },
    });

    rerender({ value: { n: 2 } });
    act(() => {
      vi.advanceTimersByTime(200);
    });
    rerender({ value: { n: 3 } });
    act(() => {
      vi.advanceTimersByTime(200);
    });
    expect(result.current).toEqual({ n: 1 });
    act(() => {
      vi.advanceTimersByTime(100);
    });
    expect(result.current).toEqual({ n: 3 });

    // Rebuilt with the same content: nothing to wait for.
    rerender({ value: { n: 3 } });
    expect(result.current).toEqual({ n: 3 });
  });
});
