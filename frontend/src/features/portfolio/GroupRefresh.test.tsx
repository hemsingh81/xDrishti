import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { setNow } from '@/test/clock';
import { answerGroupHoldings, answerGroups } from '@/test/groupFixtures';
import { server } from '@/test/server';
import { createServicesFake } from '@/test/servicesFake';
import { buildQuotesService } from '@/test/servicesFixtures';
import { renderPortfolio } from './testing';

/** 10:35 in India on Tuesday 13 Oct 2026 (a trading day), inside the session. */
const sessionNow = '2026-10-13T10:35:00+05:30';

function countLoads() {
  const loads = { groups: 0, holdings: 0 };
  server.use(
    http.get('/api/holdings/groups', ({ request }) => {
      loads.groups += 1;
      return HttpResponse.json(answerGroups(new URL(request.url).searchParams));
    }),
    http.get('/api/holdings', ({ request }) => {
      loads.holdings += 1;
      return HttpResponse.json(answerGroupHoldings(new URL(request.url).searchParams));
    }),
  );
  return loads;
}

const openEnergy = async () => {
  await userEvent.click(await screen.findByRole('button', { name: 'Energy' }));
  await screen.findByRole('gridcell', { name: 'RELIANCE' });
};

describe('Group by: reading the figures again after the prices change', () => {
  it('reads the groups and the opened group again after Refresh prices', async () => {
    const fake = createServicesFake({ services: [buildQuotesService()], runMessage: 'Quoted 4 of 4 holdings' });
    server.use(...fake.handlers);
    const loads = countLoads();
    renderPortfolio('?groupBy=sector');
    await openEnergy();
    const before = { ...loads };

    const button = await screen.findByRole('button', { name: 'Refresh prices' });
    await waitFor(() => {
      expect(button).toBeEnabled();
    });
    await userEvent.click(button);

    expect(await screen.findByText('Quoted 4 of 4 holdings')).toBeInTheDocument();
    await waitFor(() => {
      expect(loads.groups).toBeGreaterThan(before.groups);
      expect(loads.holdings).toBeGreaterThan(before.holdings);
    });
  });

  it('shows the fresh figures in the drawer after the sort changed and the prices were refreshed', async () => {
    const fake = createServicesFake({ services: [buildQuotesService()], runMessage: 'Quoted 4 of 4 holdings' });
    let price = 2_902.5;
    const lists: URLSearchParams[] = [];
    server.use(
      ...fake.handlers,
      http.get('/api/holdings', ({ request }) => {
        const params = new URL(request.url).searchParams;
        lists.push(params);
        const page = answerGroupHoldings(params);
        return HttpResponse.json({ ...page, items: page.items.map((h) => ({ ...h, lastPrice: price })) });
      }),
    );
    renderPortfolio('?groupBy=sector');
    await openEnergy();

    // The group stays open under another sort: its first list is kept in the cache, no longer in use.
    await userEvent.click(screen.getByRole('button', { name: 'Sort by Invested' }));
    await waitFor(() => {
      expect(lists.map((p) => p.get('sort'))).toContain('invested');
    });
    await screen.findByRole('gridcell', { name: 'RELIANCE' });

    price = 3_000;
    const refresh = await screen.findByRole('button', { name: 'Refresh prices' });
    await waitFor(() => {
      expect(refresh).toBeEnabled();
    });
    await userEvent.click(refresh);
    expect(await screen.findByText('Quoted 4 of 4 holdings')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('gridcell', { name: 'RELIANCE' }).closest('tr')).toHaveTextContent('₹3,000.00');
    });

    await userEvent.click(screen.getByRole('gridcell', { name: 'RELIANCE' }));

    const drawer = await screen.findByRole('dialog', { name: 'Holding details' });
    expect(within(drawer).getByText('Last price').nextElementSibling).toHaveTextContent('₹3,000.00');
    expect(within(drawer).queryByText('₹2,902.50')).not.toBeInTheDocument();
  });

  describe('while the market is open', () => {
    /** Takes the one-minute intervals over so a test can fire them: fake timers would stall the network mock. */
    function captureMinuteTimers() {
      const timers = new Map<number, () => void>();
      const realSet = globalThis.setInterval.bind(globalThis);
      const realClear = globalThis.clearInterval.bind(globalThis);
      let nextId = 9_000_000;
      vi.spyOn(globalThis, 'setInterval').mockImplementation(((callback: () => void, delay?: number) => {
        if (delay !== 60_000) return realSet(callback, delay);
        nextId += 1;
        timers.set(nextId, callback);
        return nextId;
      }) as unknown as typeof setInterval);
      vi.spyOn(globalThis, 'clearInterval').mockImplementation((id?: number) => {
        if (id !== undefined && timers.delete(id)) return;
        realClear(id);
      });
      return {
        fire: () => {
          act(() => {
            for (const callback of [...timers.values()]) callback();
          });
        },
      };
    }

    it('reads the groups and the opened group again every minute', async () => {
      setNow(sessionNow);
      const timers = captureMinuteTimers();
      const loads = countLoads();
      renderPortfolio('?groupBy=sector');
      await openEnergy();
      const before = { ...loads };

      timers.fire();

      await waitFor(() => {
        expect(loads.groups).toBeGreaterThan(before.groups);
        expect(loads.holdings).toBeGreaterThan(before.holdings);
      });
    });

    it('does not read a group that is closed', async () => {
      setNow(sessionNow);
      const timers = captureMinuteTimers();
      const loads = countLoads();
      renderPortfolio('?groupBy=sector');
      await openEnergy();
      await userEvent.click(screen.getByRole('button', { name: 'Energy' }));
      const before = { ...loads };

      timers.fire();
      await waitFor(() => {
        expect(loads.groups).toBeGreaterThan(before.groups);
      });

      expect(loads.holdings).toBe(before.holdings);
    });
  });
});
