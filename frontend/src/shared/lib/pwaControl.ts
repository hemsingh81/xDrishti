import { clearOfflineCaches } from './offlineCaches';

/** Loaded lazily, only after the capability read: ON registers the worker once the page is idle; OFF removes any earlier one. */
export async function applyPwa(enabled: boolean): Promise<void> {
  if (!enabled) {
    await clearOfflineCaches();
    return;
  }
  if (!('serviceWorker' in navigator)) return; // not a secure context: nothing to install
  await new Promise<void>((resolve) => {
    if ('requestIdleCallback' in window)
      window.requestIdleCallback(() => {
        resolve();
      });
    else setTimeout(resolve, 1);
  });
  await navigator.serviceWorker.register('/sw.js', { scope: '/', updateViaCache: 'none' });
}
