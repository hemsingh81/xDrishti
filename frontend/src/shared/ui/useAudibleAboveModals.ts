import { useEffect, type RefObject } from 'react';

/**
 * Keeps an element that is a direct child of `document.body` out of the `aria-hidden` that MUI's modals put on every sibling of
 * their own portal while open. The toast stack is a live region meant to speak over a dialog (an error raised by a Save in the
 * dialog must be announced), and it is in its own portal for exactly that reason; the modal manager hides it all the same, so
 * the attribute is taken off again as soon as it is set (the manager removes it on close as well: removing it twice is harmless).
 */
export function useAudibleAboveModals(ref: RefObject<HTMLElement | null>): void {
  useEffect(() => {
    const element = ref.current;
    if (element === null) return undefined;
    const show = () => {
      if (element.hasAttribute('aria-hidden')) element.removeAttribute('aria-hidden');
    };
    show();
    const observer = new MutationObserver(show);
    observer.observe(element, { attributes: true, attributeFilter: ['aria-hidden'] });
    return () => {
      observer.disconnect();
    };
  }, [ref]);
}
