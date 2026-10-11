import { Suspense } from 'react';
import { createBrowserRouter, type RouteObject } from 'react-router';
import { LoginPage, RequireAuth } from '@/features/auth';
import { LoadingState } from '@/shared/ui';
import { plannedNavItems } from './navigation';
import { ComingSoonPage } from './pages/ComingSoonPage';
import { NotFoundPage } from './pages/NotFoundPage';
import { RouteErrorPage } from './pages/RouteErrorPage';

// Each feature is loaded on demand (code splitting): the first page load only ships what it needs.
export const routes: RouteObject[] = [
  {
    // Public: the only screen reachable without a session.
    path: '/login',
    element: (
      <Suspense fallback={<LoadingState />}>
        <LoginPage />
      </Suspense>
    ),
    errorElement: <RouteErrorPage />,
  },
  {
    // Everything else needs a session; unauthenticated visitors are redirected to /login?next=…
    element: <RequireAuth />,
    errorElement: <RouteErrorPage />,
    children: [
      {
        path: '/',
        // The shell (sidebar, top bar, user menu) loads on demand, so the sign-in screen ships without it.
        lazy: async () => ({ Component: (await import('./layout/AppShell')).AppShell }),
        errorElement: <RouteErrorPage />,
        children: [
          { index: true, lazy: async () => ({ Component: (await import('@/features/today')).TodayPage }) },
          {
            path: 'accounts',
            lazy: async () => ({ Component: (await import('@/features/accounts')).AccountsPage }),
          },
          {
            path: 'instruments',
            lazy: async () => ({ Component: (await import('@/features/instruments')).InstrumentsPage }),
          },
          {
            path: 'portfolio',
            lazy: async () => ({ Component: (await import('@/features/portfolio')).PortfolioPage }),
          },
          {
            path: 'settings',
            lazy: async () => ({ Component: (await import('@/features/settings')).SettingsPage }),
          },
          {
            path: 'system',
            lazy: async () => ({ Component: (await import('@/features/system-status')).SystemStatusPage }),
          },
          ...plannedNavItems.map((item) => ({ path: item.path.slice(1), element: <ComingSoonPage /> })),
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
];

export const createAppRouter = () => createBrowserRouter(routes);
