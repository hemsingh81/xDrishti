import { clearOfflineCaches } from './offlineCaches';
import { applyPwa } from './pwaControl';

function install(names: string[]) {
  const unregister = vi.fn(() => Promise.resolve(true));
  const register = vi.fn(() => Promise.resolve({}));
  const deleted: string[] = [];
  Object.defineProperty(navigator, 'serviceWorker', {
    configurable: true,
    value: { register, getRegistrations: () => Promise.resolve([{ unregister }, { unregister }]) },
  });
  Object.defineProperty(globalThis, 'caches', {
    configurable: true,
    value: { keys: () => Promise.resolve(names), delete: (n: string) => (deleted.push(n), Promise.resolve(true)) },
  });
  return { unregister, register, deleted };
}

afterEach(() => {
  Reflect.deleteProperty(navigator, 'serviceWorker');
  Reflect.deleteProperty(globalThis, 'caches');
});

describe('applyPwa', () => {
  it('OFF registers nothing, unregisters earlier workers and deletes only xd- caches', async () => {
    const fake = install(['xd-offline-abc', 'keep-me']);
    await applyPwa(false);
    expect(fake.register).not.toHaveBeenCalled();
    expect(fake.unregister).toHaveBeenCalledTimes(2);
    expect(fake.deleted).toEqual(['xd-offline-abc']);
  });

  it('ON registers /sw.js at the root scope and clears nothing', async () => {
    const fake = install(['xd-offline-abc']);
    await applyPwa(true);
    expect(fake.register).toHaveBeenCalledWith('/sw.js', { scope: '/', updateViaCache: 'none' });
    expect(fake.unregister).not.toHaveBeenCalled();
    expect(fake.deleted).toEqual([]);
  });

  it('does nothing and does not throw where there is no service worker support', async () => {
    await expect(applyPwa(true)).resolves.toBeUndefined();
    await expect(applyPwa(false)).resolves.toBeUndefined();
  });

  it('sign-out clearing is the same function and swallows browser refusals', async () => {
    const fake = install(['xd-offline-abc']);
    await clearOfflineCaches();
    expect(fake.deleted).toEqual(['xd-offline-abc']);
    Object.defineProperty(navigator, 'serviceWorker', {
      configurable: true,
      value: { getRegistrations: () => Promise.reject(new Error('no')) },
    });
    await expect(clearOfflineCaches()).resolves.toBeUndefined();
  });
});
