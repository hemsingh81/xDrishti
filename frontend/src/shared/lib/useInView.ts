import { useEffect, useState, type RefObject } from 'react';

/**
 * True once the element has come near the viewport (and stays true): a heavy widget below the fold is then mounted
 * when it is about to be seen, not on page load. Without `IntersectionObserver` it is true at once, so nothing stays
 * hidden in an environment that cannot tell.
 */
export function useInView(ref: RefObject<Element | null>, { enabled = true, rootMargin = '200px' } = {}): boolean {
  const supported = typeof IntersectionObserver !== 'undefined';
  const [seen, setSeen] = useState(false);

  useEffect(() => {
    const element = ref.current;
    if (!enabled || seen || !supported || !element) return;
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) setSeen(true);
      },
      { rootMargin },
    );
    observer.observe(element);
    return () => {
      observer.disconnect();
    };
  }, [ref, enabled, seen, supported, rootMargin]);

  return !enabled || seen || !supported;
}
