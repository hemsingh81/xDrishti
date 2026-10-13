import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildGroup, buildGroups, manyHoldings } from '@/test/groupFixtures';
import { observedElements, scrollToEnd } from '@/test/intersection';
import { server } from '@/test/server';
import { smallPage } from '@/test/smallPages';
import { renderPortfolio } from './testing';

// An opened group asks for 50 holdings a page in the app (asserted in Groups.test.tsx); rendering 50-120 table rows per
// test is what makes jsdom slow, so these tests page by a few rows instead, which proves the same thing.
vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
  (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
);

const total = smallPage * 2 + smallPage / 2; // three pages, the last one half full
const symbolOf = (n: number) => `S${String(n).padStart(3, '0')}`;

function groupOf(count: number) {
  const all = manyHoldings(count);
  const requests: URLSearchParams[] = [];
  server.use(
    http.get('/api/holdings/groups', () =>
      HttpResponse.json(buildGroups({ groups: [buildGroup({ holdings: count })] })),
    ),
    http.get('/api/holdings', ({ request }) => {
      const params = new URL(request.url).searchParams;
      requests.push(params);
      const page = Number(params.get('page'));
      const size = Number(params.get('pageSize'));
      return HttpResponse.json({
        items: all.slice((page - 1) * size, page * size),
        page,
        pageSize: size,
        total: count,
      });
    }),
  );
  return requests;
}

const open = async () => {
  await userEvent.click(await screen.findByRole('button', { name: 'Energy' }));
  return screen.findByRole('grid', { name: 'Energy holdings' });
};

describe('Group by: a page of holdings at a time', () => {
  it('shows one page, says how many are shown, and loads the next page when asked, until the last', async () => {
    const requests = groupOf(total);
    renderPortfolio('?groupBy=sector');

    const grid = await open();

    await within(grid).findByRole('gridcell', { name: symbolOf(smallPage) });
    expect(within(grid).getAllByRole('row')).toHaveLength(smallPage + 1);
    expect(screen.getByText(`Showing ${String(smallPage)} of ${String(total)}`)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: `Show ${String(smallPage)} more` }));
    await within(grid).findByRole('gridcell', { name: symbolOf(smallPage * 2) });
    expect(requests.map((p) => p.get('page'))).toEqual(['1', '2']);

    await userEvent.click(screen.getByRole('button', { name: `Show ${String(smallPage / 2)} more` }));
    await within(grid).findByRole('gridcell', { name: symbolOf(total) });
    expect(screen.getByText(`Showing ${String(total)} of ${String(total)}`)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Show \d+ more/ })).not.toBeInTheDocument();
  });

  it('does not load more by scrolling: nothing inside an opened group is observed', async () => {
    const requests = groupOf(total);
    renderPortfolio('?groupBy=sector');

    const grid = await open();
    await screen.findByRole('gridcell', { name: symbolOf(smallPage) });
    const region = grid.closest<HTMLElement>('[id]')!;
    expect(observedElements().filter((element) => region.contains(element))).toEqual([]);

    scrollToEnd();
    await act(() => Promise.resolve());
    expect(requests).toHaveLength(1);
  });

  it('keeps the keyboard focus on "Show more" while the next page loads (aria-disabled, not disabled)', async () => {
    groupOf(total);
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    renderPortfolio('?groupBy=sector');
    await open();
    await screen.findByRole('gridcell', { name: symbolOf(smallPage) });
    server.use(
      http.get('/api/holdings', async ({ request }) => {
        await gate;
        const page = Number(new URL(request.url).searchParams.get('page'));
        const items = manyHoldings(total).slice((page - 1) * smallPage, page * smallPage);
        return HttpResponse.json({ items, page, pageSize: smallPage, total });
      }),
    );
    const more = screen.getByRole('button', { name: `Show ${String(smallPage)} more` });
    more.focus();

    await userEvent.keyboard('{Enter}');

    const loading = await screen.findByRole('button', { name: 'Loading…' });
    expect(loading).toBe(more);
    expect(loading).toHaveAttribute('aria-disabled', 'true');
    expect(loading).toHaveFocus();
    // A second press while it loads asks for nothing more.
    await userEvent.keyboard('{Enter}');

    release();
    await screen.findByRole('gridcell', { name: symbolOf(smallPage * 2) });
    expect(screen.getByRole('button', { name: `Show ${String(smallPage / 2)} more` })).toHaveFocus();
  });

  it('says so when the next page cannot be loaded, keeps what is shown, and retries it', async () => {
    const requests = groupOf(total);
    renderPortfolio('?groupBy=sector');
    await open();
    await screen.findByRole('gridcell', { name: symbolOf(smallPage) });

    let fail = true;
    server.use(
      http.get('/api/holdings', ({ request }) => {
        if (fail) return new HttpResponse(null, { status: 500 });
        const page = Number(new URL(request.url).searchParams.get('page'));
        const items = manyHoldings(total).slice((page - 1) * smallPage, page * smallPage);
        return HttpResponse.json({ items, page, pageSize: smallPage, total });
      }),
    );
    await userEvent.click(screen.getByRole('button', { name: `Show ${String(smallPage)} more` }));

    expect(await screen.findByText('Could not load more holdings')).toBeInTheDocument();
    expect(screen.getByText(`Showing ${String(smallPage)} of ${String(total)}`)).toBeInTheDocument();
    expect(requests).toHaveLength(1);

    fail = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('gridcell', { name: symbolOf(smallPage * 2) })).toBeInTheDocument();
  });
});
