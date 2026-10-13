import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { ReactNode } from 'react';
import { holdStep, releaseStep } from '@/test/gate';
import { csvFile, readUpload, stubUploads } from '@/test/multipart';
import { server } from '@/test/server';
import { buildImportResult, buildReport } from '@/test/tradeFixtures';
import { ToastProvider } from '@/shared/ui/ToastProvider';
import { ids } from '@/test/fixtures';
import { useImportWizard } from './hooks/useImportWizard';

stubUploads();

function setup() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>
      <ToastProvider>{children}</ToastProvider>
    </QueryClientProvider>
  );
  return renderHook(() => useImportWizard(ids.main), { wrapper });
}

describe('useImportWizard: answers that arrive after the choice changed', () => {
  it('drops a preview that was asked for another file', async () => {
    const seen: string[] = [];
    server.use(
      http.post('/api/portfolio/trades/preview', async ({ request }) => {
        seen.push((await readUpload(request)).file?.name ?? '');
        await holdStep();
        return HttpResponse.json(buildReport({ digest: 'old-file' }));
      }),
    );
    const { result } = setup();
    act(() => {
      result.current.chooseFile(csvFile('a', 'old.csv'));
    });
    let running: Promise<void> = Promise.resolve();
    act(() => {
      running = result.current.preview();
    });
    await waitFor(() => {
      expect(result.current.busy).toBe(true);
    });

    act(() => {
      result.current.chooseFile(csvFile('b', 'new.csv'));
    });
    await releaseStep();
    await running;

    expect(result.current.report).toBeNull();
    expect(result.current.file?.name).toBe('new.csv');
  });

  it('shows no “Import done” for the old file when the file changed, but still tells the owner the trades were stored', async () => {
    server.use(
      http.post('/api/portfolio/trades/preview', () => HttpResponse.json(buildReport())),
      http.post('/api/portfolio/trades/import', async () => {
        await holdStep();
        return HttpResponse.json(buildImportResult());
      }),
    );
    const { result } = setup();
    act(() => {
      result.current.chooseFile(csvFile('a', 'old.csv'));
    });
    await act(async () => {
      await result.current.preview();
    });
    await waitFor(() => {
      expect(result.current.report).not.toBeNull();
    });
    let running: Promise<void> = Promise.resolve();
    act(() => {
      running = result.current.runImport(true);
    });
    await waitFor(() => {
      expect(result.current.busy).toBe(true);
    });

    act(() => {
      result.current.chooseAccount(ids.swing);
    });
    await releaseStep();
    await running;

    expect(result.current.done).toBeNull();
    expect(result.current.report).toBeNull();
    expect(result.current.accountId).toBe(ids.swing);
    expect(await screen.findByText('Imported 10 trades. Rebuilt 1 holding, 1 lot replaced.')).toBeInTheDocument();
  });

  it('keeps a failed import visible when nothing changed meanwhile', async () => {
    server.use(
      http.post('/api/portfolio/trades/preview', () => HttpResponse.json(buildReport())),
      http.post('/api/portfolio/trades/import', () => HttpResponse.json({ title: 'Boom' }, { status: 500 })),
    );
    const { result } = setup();
    act(() => {
      result.current.chooseFile(csvFile('a', 'old.csv'));
    });
    await act(async () => {
      await result.current.preview();
    });

    await act(async () => {
      await result.current.runImport(false);
    });

    await waitFor(() => {
      expect(result.current.importing.isError).toBe(true);
    });
    expect(result.current.report).not.toBeNull();
  });
});
