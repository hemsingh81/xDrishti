import { QueryClientProvider } from '@tanstack/react-query';
import { lazy, Suspense, useState } from 'react';
import { RouterProvider } from 'react-router/dom';
import { AppThemeProvider } from '@/theme';
import { createQueryClient } from './queryClient';
import { createAppRouter } from './router';

// Devtools are loaded only in development and never shipped in the production bundle.
const QueryDevtools = import.meta.env.DEV
  ? lazy(async () => ({ default: (await import('@tanstack/react-query-devtools')).ReactQueryDevtools }))
  : () => null;

export function App() {
  const [queryClient] = useState(createQueryClient);
  const [router] = useState(createAppRouter);

  return (
    <QueryClientProvider client={queryClient}>
      <AppThemeProvider>
        <RouterProvider router={router} />
      </AppThemeProvider>
      <Suspense>
        <QueryDevtools />
      </Suspense>
    </QueryClientProvider>
  );
}
