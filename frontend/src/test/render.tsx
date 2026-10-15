import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, type RenderResult } from '@testing-library/react';
import type { ReactElement } from 'react';
import { createMemoryRouter, RouterProvider, type RouteObject } from 'react-router';
import { connectAuth, StepUpProvider } from '@/features/auth';
import { ToastProvider } from '@/shared/ui/ToastProvider';
import { AppThemeProvider } from '@/theme';

/** Renders UI with the real providers (query cache, theme, toasts, router) — tests behave like the app. */
export function renderWithProviders(ui: ReactElement, { path = '/' }: { path?: string } = {}): RoutesRender {
  return renderRoutes([{ path, element: ui }], { initialPath: path });
}

export interface RoutesRender extends RenderResult {
  router: ReturnType<typeof createMemoryRouter>;
  client: QueryClient;
}

/** The default MSW handlers sign the test owner in; override /api/auth/session to render unauthenticated. */
export function renderRoutes(
  routes: RouteObject[],
  { initialPath = '/' }: { initialPath?: string } = {},
): RoutesRender {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  connectAuth(client);
  const router = createMemoryRouter(routes, { initialEntries: [initialPath] });
  const result = render(
    <QueryClientProvider client={client}>
      <AppThemeProvider>
        <StepUpProvider>
          <ToastProvider>
            <RouterProvider router={router} />
          </ToastProvider>
        </StepUpProvider>
      </AppThemeProvider>
    </QueryClientProvider>,
  );
  return { ...result, router, client };
}
