import { useEffect } from 'react';

/**
 * Carries out a request to focus the sync bar (`?focus=sync`): calls `focus()` and, only when it found a target, tells
 * `onHandled` (which removes the request from the address). If the bar or the Account picker is not on the page yet, the
 * request stays and is tried again whenever the page changes, until it succeeds or is withdrawn.
 */
export function useSyncFocusRequest(requested: boolean, focus: () => boolean, onHandled: () => void) {
  useEffect(() => {
    if (!requested) return;
    const attempt = () => {
      if (!focus()) return false;
      onHandled();
      return true;
    };
    if (attempt()) return;
    const observer = new MutationObserver(() => {
      if (attempt()) observer.disconnect();
    });
    observer.observe(document.body, { childList: true, subtree: true });
    return () => {
      observer.disconnect();
    };
  }, [requested, focus, onHandled]);
}
