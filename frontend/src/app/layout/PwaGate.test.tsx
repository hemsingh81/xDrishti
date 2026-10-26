import { screen, waitFor } from '@testing-library/react';
import { server } from '@/test/server';
import { createCapabilitiesFake } from '@/test/capabilitiesFake';
import { renderWithProviders } from '@/test/render';
import { PwaGate } from './PwaGate';

function fakeWorker() {
  const register = vi.fn(() => Promise.resolve({}));
  const unregister = vi.fn(() => Promise.resolve(true));
  Object.defineProperty(navigator, 'serviceWorker', {
    configurable: true,
    value: { register, getRegistrations: () => Promise.resolve([{ unregister }]) },
  });
  Object.defineProperty(globalThis, 'caches', {
    configurable: true,
    value: { keys: () => Promise.resolve([]), delete: () => Promise.resolve(true) },
  });
  return { register, unregister };
}

afterEach(() => {
  Reflect.deleteProperty(navigator, 'serviceWorker');
  Reflect.deleteProperty(globalThis, 'caches');
});

describe('PwaGate', () => {
  it('OFF: registers nothing and removes an earlier registration', async () => {
    const fake = fakeWorker();
    server.use(...createCapabilitiesFake().handlers);
    renderWithProviders(<PwaGate />);
    await waitFor(() => {
      expect(fake.unregister).toHaveBeenCalled();
    });
    expect(fake.register).not.toHaveBeenCalled();
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('ON: registers only after the capability read says so', async () => {
    const fake = fakeWorker();
    const state = createCapabilitiesFake();
    state.state.items = state.state.items.map((item) => ({ ...item, enabled: item.name === 'pwa' }));
    server.use(...state.handlers);
    renderWithProviders(<PwaGate />);
    expect(fake.register).not.toHaveBeenCalled();
    await waitFor(() => {
      expect(fake.register).toHaveBeenCalledWith('/sw.js', expect.objectContaining({ scope: '/' }));
    });
    expect(fake.unregister).not.toHaveBeenCalled();
  });

  it('unknown (the read failed): counts as OFF, registers nothing and removes an earlier worker', async () => {
    const fake = fakeWorker();
    const { http, HttpResponse } = await import('msw');
    server.use(http.get('/api/capabilities', () => new HttpResponse(null, { status: 500 })));
    renderWithProviders(<PwaGate />);
    await waitFor(() => {
      expect(fake.unregister).toHaveBeenCalled();
    });
    expect(fake.register).not.toHaveBeenCalled();
  });
});
