import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { createAccountFake } from '@/test/accountFake';
import { createDataFake } from '@/test/dataFake';
import { createQualityFake } from '@/test/qualityFake';
import { createServicesFake } from '@/test/servicesFake';
import { mountLearning } from '@/test/learningFixtures';
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

  it('shows the accounts badge in the navigation drawer too, so a phone user sees what to reconnect', async () => {
    renderRoutes(routes);
    await userEvent.click(await screen.findByRole('button', { name: 'Open navigation' }));

    const navs = await screen.findAllByRole('navigation', { name: 'Main' });
    const drawer = navs[navs.length - 1];
    if (!drawer) {
      throw new Error('The navigation drawer did not open.');
    }
    // The default accounts: one connected, one never connected.
    expect(await within(drawer).findByRole('link', { name: 'Accounts, 1 needs reconnecting' })).toBeVisible();
  });

  it('shows the roadmap phase for screens that are not built yet', async () => {
    renderRoutes(routes, { initialPath: '/suggestions' });

    const note = await screen.findByText(/This screen is planned for roadmap phase/);
    expect(within(note).getByText('P9')).toBeInTheDocument();
  });

  it('opens the Learning screen (no longer a placeholder) and marks it in the navigation', async () => {
    mountLearning();
    renderRoutes(routes, { initialPath: '/learning' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Learning' })).toBeInTheDocument();
    expect(await screen.findByRole('tab', { name: 'Overview', selected: true })).toBeInTheDocument();
    expect(screen.queryByText(/planned for roadmap phase/)).not.toBeInTheDocument();
    const nav = screen.getAllByRole('navigation', { name: 'Main' })[0]!;
    expect(within(nav).getByRole('link', { name: 'Learning' })).toHaveAttribute('aria-current', 'page');
  });

  it('opens the Data quality screen (no longer a placeholder) and marks it in the navigation', async () => {
    server.use(...createQualityFake().handlers);
    renderRoutes(routes, { initialPath: '/quality' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Data quality' })).toBeInTheDocument();
    expect(await screen.findByRole('region', { name: 'Quality summary' })).toBeInTheDocument();
    expect(await screen.findByRole('grid', { name: 'Instruments' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Run checks' })).toBeInTheDocument();
    expect(screen.queryByText(/planned for roadmap phase/)).not.toBeInTheDocument();
    const nav = screen.getAllByRole('navigation', { name: 'Main' })[0]!;
    expect(within(nav).getByRole('link', { name: 'Data quality' })).toHaveAttribute('aria-current', 'page');
  });

  it('opens the Data & import screen (no longer a placeholder)', async () => {
    server.use(...createDataFake().handlers);
    renderRoutes(routes, { initialPath: '/data' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Data & import' })).toBeInTheDocument();
    expect(await screen.findByRole('region', { name: 'Data status' })).toBeInTheDocument();
    expect(await screen.findByRole('grid', { name: 'Coverage' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Update data' })).toBeInTheDocument();
    expect(screen.queryByText(/planned for roadmap phase/)).not.toBeInTheDocument();
  });

  it('opens the Services screen (no longer a placeholder) and marks it in the navigation', async () => {
    server.use(...createServicesFake().handlers);
    renderRoutes(routes, { initialPath: '/services' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Services' })).toBeInTheDocument();
    expect(await screen.findByRole('list', { name: 'Services' })).toBeInTheDocument();
    expect(await screen.findByRole('region', { name: 'Trading-day timeline' })).toBeInTheDocument();
    expect(screen.queryByText(/planned for roadmap phase/)).not.toBeInTheDocument();
    const nav = screen.getAllByRole('navigation', { name: 'Main' })[0]!;
    expect(within(nav).getByRole('link', { name: 'Services' })).toHaveAttribute('aria-current', 'page');
  });

  it('opens the Baskets screen (no longer a placeholder)', async () => {
    renderRoutes(routes, { initialPath: '/baskets' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Baskets' })).toBeInTheDocument();
    expect(await screen.findByRole('list', { name: 'Baskets' })).toBeInTheDocument();
    expect(screen.queryByText(/planned for roadmap phase/)).not.toBeInTheDocument();
  });

  it('opens the Portfolio screen (no longer a placeholder)', async () => {
    renderRoutes(routes, { initialPath: '/portfolio' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Portfolio' })).toBeInTheDocument();
    expect(await screen.findByRole('grid', { name: 'Holdings' })).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Sync now' })).toBeInTheDocument();
    expect(screen.queryByText(/planned for roadmap phase/)).not.toBeInTheDocument();
  });

  it('opens the Accounts screen (no longer a placeholder)', async () => {
    renderRoutes(routes, { initialPath: '/accounts' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Accounts' })).toBeInTheDocument();
    expect(await screen.findByRole('list', { name: 'Accounts' })).toBeInTheDocument();
    expect(screen.queryByText(/planned for roadmap phase/)).not.toBeInTheDocument();
  });

  it('opens the Instruments screen (no longer a placeholder)', async () => {
    renderRoutes(routes, { initialPath: '/instruments' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Instruments' })).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Refresh master list' })).toBeInTheDocument();
    expect(await screen.findByRole('grid', { name: 'Instruments' })).toBeInTheDocument();
    expect(screen.queryByText(/planned for roadmap phase/)).not.toBeInTheDocument();
  });

  it('opens the Settings screen (no longer a placeholder)', async () => {
    renderRoutes(routes, { initialPath: '/settings' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeInTheDocument();
    expect(await screen.findByRole('tab', { name: 'Risk profiles', selected: true })).toBeInTheDocument();
    expect(screen.queryByText(/planned for roadmap phase/)).not.toBeInTheDocument();
  });

  it('opens My account from the user menu (no sidebar entry) with its three cards', async () => {
    server.use(...createAccountFake().handlers);
    const { router } = renderRoutes(routes);

    await userEvent.click(await screen.findByRole('button', { name: /^Account menu/ }));
    await userEvent.click(await screen.findByRole('menuitem', { name: 'My account' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'My account' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/account');
    expect(await screen.findByLabelText('Display name')).toHaveValue('Test Owner');
    // The top bar names the page too, although it is not a navigation item.
    expect(screen.getByRole('heading', { level: 2, name: 'My account' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Change password' })).toBeInTheDocument();
    expect(await screen.findByRole('list', { name: 'Recent sign-in activity' })).toBeInTheDocument();
    const nav = screen.getAllByRole('navigation', { name: 'Main' })[0]!;
    expect(within(nav).queryByRole('link', { name: 'My account' })).not.toBeInTheDocument();
  });

  it('keeps /account behind the sign-in guard', async () => {
    server.use(http.get('/api/auth/session', () => problemResponse(buildProblem('auth.unauthenticated', 401), 401)));
    const { router } = renderRoutes(routes, { initialPath: '/account' });

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(router.state.location.search).toBe('?next=%2Faccount');
  });

  it('shows not-found for unknown paths', async () => {
    renderRoutes(routes, { initialPath: '/nope' });

    expect(await screen.findByRole('heading', { name: 'Page not found' })).toBeInTheDocument();
  });

  it('shows the signed-in owner in the top bar', async () => {
    renderRoutes(routes);

    await userEvent.click(await screen.findByRole('button', { name: /^Account menu/ }));

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
    expect(screen.queryByRole('button', { name: /^Account menu/ })).not.toBeInTheDocument();
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

    await userEvent.click(screen.getByRole('button', { name: /^Account menu/ }));
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
    await userEvent.click(screen.getByRole('button', { name: /^Account menu/ }));
    await userEvent.click(screen.getByRole('menuitem', { name: 'Sign out' }));
    await screen.findByRole('heading', { name: 'Sign in' });

    expect(seen).toEqual([
      { method: 'GET', csrf: null },
      { method: 'POST', csrf: 'csrf-token-1' },
    ]);
  });
});
