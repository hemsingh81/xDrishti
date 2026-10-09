import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderRoutes } from '@/test/render';
import { routes } from './router';

describe('app routes', () => {
  it('renders the Today page and opens the navigation menu', async () => {
    renderRoutes(routes);

    expect(await screen.findByRole('heading', { level: 1, name: 'Today' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Open navigation' }));
    expect(await screen.findByRole('navigation', { name: 'Main' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'System status' })).toBeInTheDocument();
  });

  it('shows the roadmap phase for screens that are not built yet', async () => {
    renderRoutes(routes, { initialPath: '/portfolio' });

    expect(await screen.findByText('P2')).toBeInTheDocument();
  });

  it('shows not-found for unknown paths', async () => {
    renderRoutes(routes, { initialPath: '/nope' });

    expect(await screen.findByRole('heading', { name: 'Page not found' })).toBeInTheDocument();
  });
});
