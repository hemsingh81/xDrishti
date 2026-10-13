import { useCallback, useState } from 'react';

const storageKey = 'xd.portfolio.actions.collapsed';

function read(): boolean | null {
  try {
    const stored = window.sessionStorage.getItem(storageKey);
    if (stored === '1') return true;
    if (stored === '0') return false;
  } catch {
    // Storage can be blocked (private mode, policy): the card then follows what is in it.
  }
  return null;
}

/**
 * The owner's choice to fold the Action center, kept for the browser session (sessionStorage, never an error when it is
 * blocked). Null until they choose: the card then follows its content (folded when nothing needs a decision).
 */
export function useActionsCollapsed(): [choice: boolean | null, set: (collapsed: boolean) => void] {
  const [choice, setChoice] = useState<boolean | null>(read);
  const set = useCallback((collapsed: boolean) => {
    setChoice(collapsed);
    try {
      window.sessionStorage.setItem(storageKey, collapsed ? '1' : '0');
    } catch {
      // Not remembered; the card still works.
    }
  }, []);
  return [choice, set];
}
