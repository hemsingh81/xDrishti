import { useCallback, useState } from 'react';

const storageKey = 'xd.portfolio.charts.collapsed';

/** Below this window height (px) the card starts collapsed: with the history above it, the groups would be out of sight. */
export const compactWindowHeight = 1000;

function read(): boolean {
  try {
    const stored = window.sessionStorage.getItem(storageKey);
    if (stored === '1') return true;
    if (stored === '0') return false;
  } catch {
    // Storage can be blocked (private mode, policy): the card then follows the window height.
  }
  return window.innerHeight < compactWindowHeight;
}

/**
 * Whether the Charts card is collapsed. Until the owner chooses it follows the window (open on a tall one, collapsed on a
 * short one); the choice is remembered for the browser session (sessionStorage, never an error if it is blocked).
 */
export function useChartsCollapsed(): [collapsed: boolean, toggle: () => void] {
  const [collapsed, setCollapsed] = useState(read);
  const toggle = useCallback(() => {
    setCollapsed((previous) => {
      try {
        window.sessionStorage.setItem(storageKey, previous ? '0' : '1');
      } catch {
        // Not remembered; the card still works.
      }
      return !previous;
    });
  }, []);
  return [collapsed, toggle];
}
