import { createBrowserRouter, type RouteObject } from 'react-router';
import { AppShell } from './layout/AppShell';
import { plannedNavItems } from './navigation';
import { ComingSoonPage } from './pages/ComingSoonPage';
import { NotFoundPage } from './pages/NotFoundPage';
import { RouteErrorPage } from './pages/RouteErrorPage';

// Each feature is loaded on demand (code splitting): the first page load only ships what it needs.
export const routes: RouteObject[] = [
  {
    path: '/',
    element: <AppShell />,
    errorElement: <RouteErrorPage />,
    children: [
      { index: true, lazy: async () => ({ Component: (await import('@/features/today')).TodayPage }) },
      {
        path: 'system',
        lazy: async () => ({ Component: (await import('@/features/system-status')).SystemStatusPage }),
      },
      ...plannedNavItems.map((item) => ({ path: item.path.slice(1), element: <ComingSoonPage /> })),
      { path: '*', element: <NotFoundPage /> },
    ],
  },
];

export const createAppRouter = () => createBrowserRouter(routes);
