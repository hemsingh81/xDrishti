import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { Suspense } from 'react';
import type { RouteObject } from 'react-router';
import { buildProblem, buildSession } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { LoginPage, RequireAuth } from './index';

const routes: RouteObject[] = [
  {
    path: '/login',
    element: (
      <Suspense>
        <LoginPage />
      </Suspense>
    ),
  },
  {
    element: <RequireAuth />,
    children: [
      { path: '/', element: <h1>Home page</h1> },
      { path: '/system', element: <h1>System page</h1> },
    ],
  },
];

const signedOut = () => {
  server.use(http.get('/api/auth/session', () => problemResponse(buildProblem('auth.unauthenticated', 401), 401)));
};

async function signIn(email = 'example@invalid.test', password = 'not-a-real-password') {
  await userEvent.type(await screen.findByLabelText('Email'), email);
  await userEvent.type(screen.getByLabelText('Password'), password);
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
}

describe('sign-in page', () => {
  beforeEach(signedOut);

  it('signs in and lands on the page the user asked for', async () => {
    let body: unknown;
    server.use(
      http.post('/api/auth/login', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(buildSession());
      }),
    );
    const { router } = renderRoutes(routes, { initialPath: '/login?next=%2Fsystem' });

    await signIn();

    expect(await screen.findByRole('heading', { name: 'System page' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/system');
    expect(body).toEqual({ email: 'example@invalid.test', password: 'not-a-real-password' });
  });

  it('lands on the home page when there is no next', async () => {
    server.use(http.post('/api/auth/login', () => HttpResponse.json(buildSession())));
    renderRoutes(routes, { initialPath: '/login' });

    await signIn();

    expect(await screen.findByRole('heading', { name: 'Home page' })).toBeInTheDocument();
  });

  it.each(['//evil.test', 'https://evil.test', 'javascript:alert(1)'])(
    'ignores the unsafe next=%s and lands on the home page',
    async (next) => {
      server.use(http.post('/api/auth/login', () => HttpResponse.json(buildSession())));
      const { router } = renderRoutes(routes, { initialPath: `/login?next=${encodeURIComponent(next)}` });

      await signIn();

      expect(await screen.findByRole('heading', { name: 'Home page' })).toBeInTheDocument();
      expect(router.state.location.pathname).toBe('/');
    },
  );

  it('has labelled fields, no prefilled values, and a working show/hide password toggle', async () => {
    renderRoutes(routes, { initialPath: '/login' });

    const email = await screen.findByLabelText('Email');
    const password = screen.getByLabelText('Password');
    expect(email).toHaveValue('');
    expect(email).toHaveAttribute('autocomplete', 'username');
    expect(password).toHaveValue('');
    expect(password).toHaveAttribute('autocomplete', 'current-password');
    expect(password).toHaveAttribute('type', 'password');

    await userEvent.click(screen.getByRole('button', { name: 'Show password' }));
    expect(password).toHaveAttribute('type', 'text');
    await userEvent.click(screen.getByRole('button', { name: 'Hide password' }));
    expect(password).toHaveAttribute('type', 'password');
  });

  it('asks for the missing fields without calling the API', async () => {
    renderRoutes(routes, { initialPath: '/login' });

    await userEvent.click(await screen.findByRole('button', { name: 'Sign in' }));

    expect(screen.getByText('Enter your email address.')).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Email'), 'example@invalid.test');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(screen.getByText('Enter your password.')).toBeInTheDocument();
  });

  it('says the credentials are wrong without revealing which part', async () => {
    server.use(http.post('/api/auth/login', () => problemResponse(buildProblem('auth.invalid_credentials', 401), 401)));
    renderRoutes(routes, { initialPath: '/login' });

    await signIn();

    expect(await screen.findByRole('alert')).toHaveTextContent('The email or password is incorrect.');
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeEnabled();
  });

  it('keeps focus on the password field while the request is pending', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.post('/api/auth/login', async () => {
        await gate;
        return HttpResponse.json(buildSession());
      }),
    );
    renderRoutes(routes, { initialPath: '/login' });

    await userEvent.type(await screen.findByLabelText('Email'), 'example@invalid.test');
    await userEvent.type(screen.getByLabelText('Password'), 'not-a-real-password{Enter}');

    const password = screen.getByLabelText('Password');
    await waitFor(() => {
      expect(password).toHaveAttribute('aria-disabled', 'true');
    });
    expect(password).toHaveFocus();
    expect(password).toHaveAttribute('readonly');

    release();
    expect(await screen.findByRole('heading', { name: 'Home page' })).toBeInTheDocument();
  });

  it('shows a rate-limit message and blocks the form while it applies', async () => {
    server.use(
      http.post('/api/auth/login', () =>
        problemResponse(buildProblem('auth.rate_limited', 429, { retryAfterSeconds: 45 }), 429, {
          'Retry-After': '45',
        }),
      ),
    );
    renderRoutes(routes, { initialPath: '/login' });

    await signIn();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Too many sign-in attempts');
    expect(within(alert).getByText('0:45')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeDisabled();
  });

  it('shows the session-expired notice when the previous session ended', async () => {
    server.use(http.get('/api/auth/session', () => problemResponse(buildProblem('auth.session_expired', 401), 401)));
    renderRoutes(routes, { initialPath: '/system' });

    expect(await screen.findByText('Your session expired — please sign in again.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
  });
});

describe('sign-in page lock countdown', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('counts down the lock and re-enables the form at zero', async () => {
    signedOut();
    server.use(
      http.post('/api/auth/login', () =>
        problemResponse(buildProblem('auth.locked', 423, { retryAfterSeconds: 3 }), 423, { 'Retry-After': '3' }),
      ),
    );
    // Only the clock is faked: network (MSW) and Testing Library polling keep running on real timers.
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    renderRoutes(routes, { initialPath: '/login' });

    await signIn();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('sign-in is locked');
    expect(within(alert).getByText('0:03')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeDisabled();
    expect(screen.getByLabelText('Email')).toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByLabelText('Email')).toHaveAttribute('readonly');

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1000);
    });
    expect(within(screen.getByRole('alert')).getByText('0:02')).toBeInTheDocument();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    await waitFor(() => {
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeEnabled();
    expect(screen.getByLabelText('Email')).not.toHaveAttribute('readonly');
    expect(screen.getByLabelText('Email')).not.toHaveAttribute('aria-disabled');
    expect(screen.getByLabelText('Email')).toHaveFocus();
  });
});

describe('route guard', () => {
  it('redirects an unauthenticated visitor to /login keeping where they wanted to go', async () => {
    signedOut();
    const { router } = renderRoutes(routes, { initialPath: '/system?tab=db' });

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/login');
    expect(router.state.location.search).toBe(`?next=${encodeURIComponent('/system?tab=db')}`);
  });

  it('sends a signed-in visitor of /login on to next', async () => {
    const { router } = renderRoutes(routes, { initialPath: '/login?next=%2Fsystem' });

    expect(await screen.findByRole('heading', { name: 'System page' })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/system');
  });

  it('shows the protected page when signed in', async () => {
    renderRoutes(routes, { initialPath: '/system' });

    expect(await screen.findByRole('heading', { name: 'System page' })).toBeInTheDocument();
  });

  it('shows an error with a retry when the session cannot be checked', async () => {
    server.use(http.get('/api/auth/session', () => HttpResponse.json({ title: 'Down' }, { status: 503 })));
    renderRoutes(routes, { initialPath: '/system' });

    expect(await screen.findByText('Down')).toBeInTheDocument();
    server.resetHandlers();
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByRole('heading', { name: 'System page' })).toBeInTheDocument();
  });
});
