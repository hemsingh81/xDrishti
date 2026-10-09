import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, type RenderResult } from '@testing-library/react';
import type { ReactElement } from 'react';
import { createMemoryRouter, RouterProvider, type RouteObject } from 'react-router';
import { AppThemeProvider } from '@/theme';

/** Renders UI with the real providers (query cache, theme, router) — tests behave like the app. */
export function renderWithProviders(ui: ReactElement, { path = '/' }: { path?: string } = {}): RenderResult {
  return renderRoutes([{ path, element: ui }], { initialPath: path });
}

export function renderRoutes(
  routes: RouteObject[],
  { initialPath = '/' }: { initialPath?: string } = {},
): RenderResult {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity } } });
  const router = createMemoryRouter(routes, { initialEntries: [initialPath] });
  return render(
    <QueryClientProvider client={client}>
      <AppThemeProvider>
        <RouterProvider router={router} />
      </AppThemeProvider>
    </QueryClientProvider>,
  );
}
