import { useSyncExternalStore } from 'react';

const subscribe = (notify: () => void): (() => void) => {
  document.addEventListener('visibilitychange', notify);
  return () => {
    document.removeEventListener('visibilitychange', notify);
  };
};

const snapshot = (): boolean => !document.hidden;

/** Whether the tab is shown (not in the background, not minimised). */
export function usePageVisible(): boolean {
  return useSyncExternalStore(subscribe, snapshot, () => true);
}
