/** Unregisters every service worker and deletes every `xd-` cache (sign-out, expiry, switch OFF). Best effort: never throws. */
export async function clearOfflineCaches(): Promise<void> {
  try {
    if ('serviceWorker' in navigator) {
      const registrations = await navigator.serviceWorker.getRegistrations();
      await Promise.all(registrations.map((registration) => registration.unregister()));
    }
    if ('caches' in globalThis) {
      const names = await caches.keys();
      await Promise.all(names.filter((name) => name.startsWith('xd-')).map((name) => caches.delete(name)));
    }
  } catch {
    // Nothing to clear, or the browser refused: the worker caches no data in any case.
  }
}
