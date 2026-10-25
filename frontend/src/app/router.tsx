import { Suspense } from 'react';
import { createBrowserRouter, type RouteObject } from 'react-router';
import { AccountPage, LoginPage, RequireAuth } from '@/features/auth';
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
            path: 'data',
            lazy: async () => ({ Component: (await import('@/features/data')).DataPage }),
          },
          {
            path: 'quality',
            lazy: async () => ({ Component: (await import('@/features/quality')).QualityPage }),
          },
          {
            path: 'services',
            lazy: async () => ({ Component: (await import('@/features/services')).ServicesPage }),
          },
          { path: 'audit', lazy: async () => ({ Component: (await import('@/features/audit')).AuditPage }) },
          {
            path: 'baskets',
            lazy: async () => ({ Component: (await import('@/features/baskets')).BasketsPage }),
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
            // Not in the sidebar: opened from the user menu and the command palette. The page is a lazy component
            // (auth is already in the initial bundle, so a second dynamic import of it would not split anything).
            path: 'account',
            Component: AccountPage,
          },
          {
            path: 'lab',
            lazy: async () => ({ Component: (await import('@/features/lab')).LabPage }),
          },
          {
            path: 'learning',
            lazy: async () => ({ Component: (await import('@/features/learning')).LearningPage }),
          },
          {
            path: 'reports',
            lazy: async () => ({ Component: (await import('@/features/reports')).ReportsPage }),
          },
          {
            path: 'plans',
            lazy: async () => ({ Component: (await import('@/features/plans')).PlansPage }),
          },
          {
            path: 'paper/governance',
            lazy: async () => ({ Component: (await import('@/features/paper-governance')).GovernancePage }),
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
