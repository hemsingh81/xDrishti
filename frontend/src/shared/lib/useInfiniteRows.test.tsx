import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { useInfiniteRows, type PageResult } from './useInfiniteRows';

interface Item {
  id: string;
}

const everything = Array.from({ length: 250 }, (_, i) => ({ id: `r${String(i + 1)}` }));

/** A stub endpoint over 250 rows that pages by `page` / `pageSize`, like the real ones. */
function endpoint(list: Item[] = everything) {
  return vi.fn((page: number, { pageSize }: { pageSize: number }): Promise<PageResult<Item>> =>
    Promise.resolve({ items: list.slice((page - 1) * pageSize, page * pageSize), total: list.length, page, pageSize }),
  );
}

function setup(props: Parameters<typeof useInfiniteRows<Item>>[0]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, ...renderHook((p: typeof props) => useInfiniteRows<Item>(p), { wrapper, initialProps: props }) };
}

describe('useInfiniteRows', () => {
  it('loads the first page, then appends the next ones until the total is reached', async () => {
    const fetchPage = endpoint();
    const { result } = setup({ queryKey: ['t', 'list'], fetchPage, pageSize: 100 });

    expect(result.current.isPending).toBe(true);
    await waitFor(() => {
      expect(result.current.rows).toHaveLength(100);
    });
    expect(result.current).toMatchObject({ total: 250, hasMore: true, isPending: false, isError: false });

    act(() => {
      result.current.loadMore();
    });
    await waitFor(() => {
      expect(result.current.rows).toHaveLength(200);
    });
    act(() => {
      result.current.loadMore();
    });
    await waitFor(() => {
      expect(result.current.rows).toHaveLength(250);
    });
    expect(result.current.hasMore).toBe(false);
    expect(result.current.rows.map((r) => r.id)).toEqual(everything.map((r) => r.id));
    expect(fetchPage.mock.calls.map(([page]) => page)).toEqual([1, 2, 3]);
  });

  it('asks the fetcher for the requested page size, 100 by default', async () => {
    const fetchPage = endpoint();
    setup({ queryKey: ['t', 'default'], fetchPage });
    await waitFor(() => {
      expect(fetchPage).toHaveBeenCalledWith(1, expect.objectContaining({ pageSize: 100 }));
    });
  });

  it('requests one page at a time, and nothing once there are no more', async () => {
    const fetchPage = endpoint(everything.slice(0, 150));
    const { result } = setup({ queryKey: ['t', 'once'], fetchPage });
    await waitFor(() => {
      expect(result.current.hasMore).toBe(true);
    });

    act(() => {
      result.current.loadMore();
      result.current.loadMore();
    });
    await waitFor(() => {
      expect(result.current.rows).toHaveLength(150);
    });
    act(() => {
      result.current.loadMore();
    });

    expect(fetchPage).toHaveBeenCalledTimes(2);
  });

  it('lists a row once when pages overlap and a row id is given', async () => {
    const overlapping = vi.fn((page: number): Promise<PageResult<Item>> =>
      Promise.resolve({
        items: page === 1 ? [{ id: 'a' }, { id: 'b' }] : [{ id: 'b' }, { id: 'c' }],
        total: 4,
        page,
        pageSize: 2,
      }),
    );
    const { result } = setup({ queryKey: ['t', 'dupes'], fetchPage: overlapping, pageSize: 2, getRowId: (i) => i.id });
    await waitFor(() => {
      expect(result.current.rows).toHaveLength(2);
    });

    act(() => {
      result.current.loadMore();
    });

    await waitFor(() => {
      expect(result.current.rows.map((r) => r.id)).toEqual(['a', 'b', 'c']);
    });
  });

  it('starts a new list for a new key and does not offer more of the old rows meanwhile', async () => {
    const fetchPage = endpoint();
    const { result, rerender } = setup({ queryKey: ['t', 'a'], fetchPage });
    await waitFor(() => {
      expect(result.current.rows).toHaveLength(100);
    });
    let release: () => void = () => undefined;
    const slow = vi.fn(
      (page: number, { pageSize }: { pageSize: number }) =>
        new Promise<PageResult<Item>>((resolve) => {
          release = () => {
            resolve({ items: [{ id: 'x1' }], total: 1, page, pageSize });
          };
        }),
    );

    rerender({ queryKey: ['t', 'b'], fetchPage: slow });

    await waitFor(() => {
      expect(result.current.isPlaceholderData).toBe(true);
    });
    expect(result.current.rows).toHaveLength(100); // the previous list, marked as such
    expect(result.current.hasMore).toBe(false);
    act(() => {
      result.current.loadMore();
    });
    expect(slow).toHaveBeenCalledTimes(1); // only page 1 of the new list

    act(() => {
      release();
    });
    await waitFor(() => {
      expect(result.current.rows).toEqual([{ id: 'x1' }]);
    });
    expect(result.current).toMatchObject({ isPlaceholderData: false, total: 1, hasMore: false });
  });

  it('shows nothing of the old list when previous rows are not kept', async () => {
    const { result, rerender } = setup({ queryKey: ['t', 'k1'], fetchPage: endpoint(), keepPreviousRows: false });
    await waitFor(() => {
      expect(result.current.rows).toHaveLength(100);
    });

    rerender({ queryKey: ['t', 'k2'], fetchPage: () => new Promise(() => undefined), keepPreviousRows: false });

    await waitFor(() => {
      expect(result.current.rows).toHaveLength(0);
    });
    expect(result.current.isPending).toBe(true);
  });

  it('reports a failed first page as an error with nothing to show, and a retry reloads it', async () => {
    let fail = true;
    const fetchPage = vi.fn((page: number, ctx: { pageSize: number }) =>
      fail ? Promise.reject(new Error('down')) : endpoint()(page, ctx),
    );
    const { result } = setup({ queryKey: ['t', 'bad'], fetchPage });

    await waitFor(() => {
      expect(result.current.isError).toBe(true);
    });
    expect(result.current).toMatchObject({ rows: [], loadMoreError: null });
    expect(result.current.error?.message).toBe('down');

    fail = false;
    act(() => {
      result.current.refetch();
    });
    await waitFor(() => {
      expect(result.current.rows).toHaveLength(100);
    });
    expect(result.current.isError).toBe(false);
  });

  it('keeps the loaded rows when the next page fails, reports it as loadMoreError and retries that page', async () => {
    let fail = false;
    const base = endpoint();
    const fetchPage = vi.fn((page: number, ctx: { pageSize: number; signal: AbortSignal }) =>
      fail && page === 2 ? Promise.reject(new Error('page 2 down')) : base(page, ctx),
    );
    const { result } = setup({ queryKey: ['t', 'more'], fetchPage });
    await waitFor(() => {
      expect(result.current.rows).toHaveLength(100);
    });

    fail = true;
    act(() => {
      result.current.loadMore();
    });
    await waitFor(() => {
      expect(result.current.loadMoreError?.message).toBe('page 2 down');
    });
    expect(result.current.rows).toHaveLength(100);
    expect(result.current.isError).toBe(false);

    fail = false;
    act(() => {
      result.current.retryMore();
    });
    await waitFor(() => {
      expect(result.current.rows).toHaveLength(200);
    });
    expect(result.current.loadMoreError).toBeNull();
  });

  it('does not offer a further page while the loaded pages are refreshed, and offers it again afterwards', async () => {
    let gate: Promise<void> = Promise.resolve();
    const base = endpoint();
    const fetchPage = vi.fn(async (page: number, ctx: { pageSize: number; signal: AbortSignal }) => {
      await gate;
      return base(page, ctx);
    });
    const { result, client } = setup({ queryKey: ['t', 'refresh'], fetchPage });
    await waitFor(() => {
      expect(result.current.hasMore).toBe(true);
    });

    let open: () => void = () => undefined;
    gate = new Promise<void>((resolve) => {
      open = resolve;
    });
    act(() => {
      void client.invalidateQueries({ queryKey: ['t', 'refresh'] });
    });
    await waitFor(() => {
      expect(result.current.hasMore).toBe(false);
    });
    expect(result.current.rows).toHaveLength(100); // the rows stay meanwhile

    act(() => {
      open();
    });
    await waitFor(() => {
      expect(result.current.hasMore).toBe(true);
    });
  });
  it('hands over the newest page as the endpoint sent it, for what it carries besides the rows', async () => {
    const base = endpoint();
    const fetchPage = vi.fn(async (page: number, ctx: { pageSize: number; signal: AbortSignal }) => ({
      ...(await base(page, ctx)),
      counts: { page },
    }));
    const { result } = setup({ queryKey: ['t', 'last'], fetchPage });
    expect(result.current.lastPage).toBeUndefined();
    await waitFor(() => {
      expect(result.current.lastPage).toMatchObject({ page: 1, counts: { page: 1 } });
    });

    act(() => {
      result.current.loadMore();
    });
    await waitFor(() => {
      expect(result.current.lastPage).toMatchObject({ page: 2 });
    });
  });
});
