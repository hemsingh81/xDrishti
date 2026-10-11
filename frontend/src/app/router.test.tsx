import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildProblem, buildSystemStatus } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
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

  it('opens the Accounts screen (no longer a placeholder)', async () => {
    renderRoutes(routes, { initialPath: '/accounts' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Accounts' })).toBeInTheDocument();
    expect(await screen.findByRole('list', { name: 'Accounts' })).toBeInTheDocument();
    expect(screen.queryByText(/planned for roadmap phase/)).not.toBeInTheDocument();
  });

  it('opens the Settings screen (no longer a placeholder)', async () => {
    renderRoutes(routes, { initialPath: '/settings' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeInTheDocument();
    expect(await screen.findByRole('tab', { name: 'Risk profiles', selected: true })).toBeInTheDocument();
    expect(screen.queryByText(/planned for roadmap phase/)).not.toBeInTheDocument();
  });

  it('shows not-found for unknown paths', async () => {
    renderRoutes(routes, { initialPath: '/nope' });

    expect(await screen.findByRole('heading', { name: 'Page not found' })).toBeInTheDocument();
  });

  it('shows the signed-in owner in the top bar', async () => {
    renderRoutes(routes);

    await userEvent.click(await screen.findByRole('button', { name: 'Account menu' }));

    expect(screen.getByText('example@invalid.test')).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Sign out' })).toBeInTheDocument();
  });
});

describe('authentication', () => {
  it('redirects an unauthenticated visitor to the login page and keeps the target', async () => {
    server.use(http.get('/api/auth/session', () => problemResponse(buildProblem('auth.unauthenticated', 401), 401)));
    const { router } = renderRoutes(routes, { initialPath: '/system' });

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(router.state.location.search).toBe('?next=%2Fsystem');
    expect(screen.queryByRole('button', { name: 'Account menu' })).not.toBeInTheDocument();
  });

  it('sends the user to the login page when any API call answers 401', async () => {
    server.use(http.get('/api/system/status', () => problemResponse(buildProblem('auth.session_expired', 401), 401)));
    const { router } = renderRoutes(routes, { initialPath: '/system' });

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/login');
    expect(router.state.location.search).toBe('?next=%2Fsystem');
    expect(screen.getByText('Your session expired — please sign in again.')).toBeInTheDocument();
  });

  it('signs out: sends the CSRF token, clears cached data and returns to the login page', async () => {
    let csrf: string | null = null;
    server.use(
      http.post('/api/auth/logout', ({ request }) => {
        csrf = request.headers.get('X-CSRF-Token');
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { router, client } = renderRoutes(routes, { initialPath: '/system' });
    expect(await screen.findByText('PostgreSQL 18.6')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Account menu' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Sign out' }));

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(csrf).toBe('csrf-token-1');
    expect(router.state.location.pathname).toBe('/login');
    expect(router.state.location.search).toBe('');
    expect(client.getQueryData(['system', 'status'])).toBeUndefined();
    expect(screen.queryByText('Your session expired — please sign in again.')).not.toBeInTheDocument();
  });

  it('attaches the CSRF token to state-changing requests but not to reads', async () => {
    const seen: { method: string; csrf: string | null }[] = [];
    server.use(
      http.get('/api/system/status', ({ request }) => {
        seen.push({ method: request.method, csrf: request.headers.get('X-CSRF-Token') });
        return HttpResponse.json(buildSystemStatus());
      }),
      http.post('/api/auth/logout', ({ request }) => {
        seen.push({ method: request.method, csrf: request.headers.get('X-CSRF-Token') });
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderRoutes(routes, { initialPath: '/system' });
    await screen.findByText('PostgreSQL 18.6');
    await userEvent.click(screen.getByRole('button', { name: 'Account menu' }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Sign out' }));
    await screen.findByRole('heading', { name: 'Sign in' });

    expect(seen).toEqual([
      { method: 'GET', csrf: null },
      { method: 'POST', csrf: 'csrf-token-1' },
    ]);
  });
});
