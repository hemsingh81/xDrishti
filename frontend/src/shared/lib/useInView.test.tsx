import { renderHook } from '@testing-library/react';
import { scrollToEnd } from '@/test/intersection';
import { useInView } from './useInView';

describe('useInView', () => {
  it('turns true when the element comes near the viewport, and stays true', () => {
    const element = document.createElement('div');
    const { result } = renderHook(() => useInView({ current: element }));
    expect(result.current).toBe(false);

    scrollToEnd();

    expect(result.current).toBe(true);
  });

  it('is true at once when it is not enabled', () => {
    const element = document.createElement('div');
    const { result } = renderHook(() => useInView({ current: element }, { enabled: false }));
    expect(result.current).toBe(true);
  });

  it('is true at once when the browser cannot tell', () => {
    vi.stubGlobal('IntersectionObserver', undefined);
    const element = document.createElement('div');
    const { result } = renderHook(() => useInView({ current: element }));
    expect(result.current).toBe(true);
    vi.unstubAllGlobals();
  });
});
