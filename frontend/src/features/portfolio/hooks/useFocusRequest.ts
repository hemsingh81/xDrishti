import { useCallback, useEffect, useRef, useState } from 'react';

/**
 * Puts the keyboard on an element after the next render. A dialog that closes hands the focus back to the button that opened
 * it (in an effect of its own, which runs first); when that button is gone by then (a Void button of a row that is voided)
 * the focus would be lost. Asking for the focus here, in the same render, lets the element have the last word.
 * Returns `[ref, request]`: `ref` goes on the element, `request()` asks for the focus.
 */
export function useFocusRequest<T extends HTMLElement>() {
  const ref = useRef<T>(null);
  const [requests, setRequests] = useState(0);
  useEffect(() => {
    if (requests > 0) ref.current?.focus();
  }, [requests]);
  const request = useCallback(() => {
    setRequests((count) => count + 1);
  }, []);
  return [ref, request] as const;
}
