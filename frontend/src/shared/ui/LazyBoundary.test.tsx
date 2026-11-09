import { render, screen } from '@testing-library/react';
import { lazy } from 'react';
import { LazyBoundary } from './LazyBoundary';

describe('LazyBoundary', () => {
  it('shows the pending content, then the loaded component', async () => {
    const Ok = lazy(() => Promise.resolve({ default: () => <p>Loaded</p> }));
    render(
      <LazyBoundary pending={<p>Waiting</p>}>
        <Ok />
      </LazyBoundary>,
    );
    expect(screen.getByText('Waiting')).toBeInTheDocument();
    expect(await screen.findByText('Loaded')).toBeInTheDocument();
  });

  it('keeps a failed chunk local: the given fallback, or a message with Reload', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const Broken = lazy(() => Promise.reject(new Error('chunk failed')));
    const { unmount } = render(
      <LazyBoundary pending={<p>Waiting</p>} failed={<button type="button">Sign out</button>}>
        <Broken />
      </LazyBoundary>,
    );
    expect(await screen.findByRole('button', { name: 'Sign out' })).toBeInTheDocument();
    unmount();

    const Broken2 = lazy(() => Promise.reject(new Error('chunk failed')));
    render(
      <LazyBoundary pending={<p>Waiting</p>}>
        <Broken2 />
      </LazyBoundary>,
    );
    expect(await screen.findByRole('button', { name: 'Reload' })).toBeInTheDocument();
    expect(screen.getByText(/could not load/)).toBeInTheDocument();
  });
});
