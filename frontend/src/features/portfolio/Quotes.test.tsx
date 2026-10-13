import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildSummary, holdingIds, sampleHoldings, searchHoldings } from '@/test/fixtures';
import { passMinutes, setNow } from '@/test/clock';
import { problemResponse, server } from '@/test/server';
import { createServicesFake } from '@/test/servicesFake';
import { buildQuotesService, buildServiceRun, buildTimeline, serviceRunId } from '@/test/servicesFixtures';
import type { HoldingsSummary, ServiceView } from '@/shared/api';
import { renderPortfolio } from './testing';

/** 10:35 in India on Tuesday 13 Oct 2026 (a trading day), inside the session. */
const sessionNow = '2026-10-13T10:35:00+05:30';
const iso = (hhmm: string, day = '2026-10-13') => `${day}T${hhmm}:00+05:30`;

const summaryWith = (overrides: Partial<HoldingsSummary>) =>
  http.get('/api/holdings/summary', () => HttpResponse.json(buildSummary(overrides)));

const listWith = (service: ServiceView) => http.get('/api/services', () => HttpResponse.json({ items: [service] }));

/** The prices line is found by its words, the way the owner reads it. */
const pricesLine = (text: string | RegExp) => screen.findByText(text);

describe('Portfolio: day change in the totals', () => {
  it('shows the day change with sign, arrow and percentage', async () => {
    setNow('2026-10-11T10:30:00Z'); // the day of the prices
    renderPortfolio();

    const tile = (await screen.findByText('Day change', { selector: 'p' })).closest('.MuiCard-root');
    expect(tile).not.toBeNull();
    expect(within(tile as HTMLElement).getByText('+₹900')).toHaveTextContent('▲+₹900');
    expect(within(tile as HTMLElement).getByText('+0.56%')).toHaveTextContent('▲+0.56%');
    expect(within(tile as HTMLElement).queryByText(/as of/)).not.toBeInTheDocument();
  });

  it('shows a loss with a minus sign and a down arrow', async () => {
    server.use(summaryWith({ dayChange: -1_250, dayChangePct: -1.2 }));
    setNow('2026-10-11T10:30:00Z');
    renderPortfolio();

    expect(await screen.findByText('−₹1,250')).toHaveTextContent('▼−₹1,250');
    expect(screen.getByText('−1.20%')).toHaveTextContent('▼−1.20%');
  });

  it('labels a day change that belongs to an earlier India day with its date', async () => {
    setNow('2026-10-13T05:05:00Z');
    renderPortfolio();

    const tile = (await screen.findByText('Day change', { selector: 'p' })).closest('.MuiCard-root');
    expect(await within(tile as HTMLElement).findByText(/· as of 11 Oct/)).toBeInTheDocument();
  });

  it('shows a dash and says why when no holding has a previous close', async () => {
    server.use(summaryWith({ dayChange: null, dayChangePct: null, dayChangeExcluded: 4 }));
    renderPortfolio();

    expect(await screen.findByText('No previous close yet')).toBeInTheDocument();
    const reason = screen.getAllByRole('img', { name: /No holding has a previous close yet/ })[0];
    expect(reason).toHaveTextContent('—');
    await userEvent.hover(reason!);
    expect(await screen.findByRole('tooltip')).toHaveTextContent(/press Refresh prices/);
  });

  it('notes how many holdings the day change leaves out', async () => {
    renderPortfolio();
    expect(await screen.findByText('Day change excludes 1 holding without a previous close')).toBeInTheDocument();
  });

  it('says "holdings" for several, and nothing when none is left out', async () => {
    server.use(summaryWith({ dayChangeExcluded: 3 }));
    const { unmount } = renderPortfolio();
    expect(await screen.findByText('Day change excludes 3 holdings without a previous close')).toBeInTheDocument();
    unmount();

    server.use(summaryWith({ dayChangeExcluded: 0 }));
    renderPortfolio();
    await screen.findByText('₹1,85,000');
    expect(screen.queryByText(/Day change excludes/)).not.toBeInTheDocument();
  });
});

describe('Portfolio: day change in the list and the drawer', () => {
  it('asks the API to sort by day change, both ways, and keeps it in the URL', async () => {
    const sorts: (string | null)[] = [];
    server.use(
      http.get('/api/holdings', ({ request }) => {
        const url = new URL(request.url);
        sorts.push(url.searchParams.get('sort'));
        return HttpResponse.json(searchHoldings(sampleHoldings, url.searchParams));
      }),
    );
    const { router } = renderPortfolio();
    await screen.findByRole('gridcell', { name: 'RELIANCE' });

    const header = screen.getByRole('columnheader', { name: 'Day change' });
    await userEvent.click(within(header).getByRole('button'));
    await vi.waitFor(() => {
      expect(sorts).toContain('dayChange');
    });
    await userEvent.click(within(screen.getByRole('columnheader', { name: 'Day change' })).getByRole('button'));
    await vi.waitFor(() => {
      expect(sorts).toContain('-dayChange');
    });
    expect(router.state.location.search).toBe('?sort=-dayChange');
    expect(screen.getByRole('columnheader', { name: 'Day change' })).toHaveAttribute('aria-sort', 'descending');
  });

  it('accepts the percentage sort field of the API from a link', async () => {
    const sorts: (string | null)[] = [];
    server.use(
      http.get('/api/holdings', ({ request }) => {
        sorts.push(new URL(request.url).searchParams.get('sort'));
        return HttpResponse.json(searchHoldings(sampleHoldings, new URL(request.url).searchParams));
      }),
    );
    renderPortfolio('?sort=-dayChangePct');
    await screen.findByRole('gridcell', { name: 'RELIANCE' });

    expect(sorts[0]).toBe('-dayChangePct');
  });

  it('shows rupees over percent, a dash with the reason when there is no previous close, and the session date of an old price', async () => {
    setNow('2026-10-12T05:00:00Z'); // the day after the prices
    renderPortfolio();

    const row = await screen.findByRole('row', { name: /^RELIANCE\b/ });
    const cell = within(row).getAllByRole('gridcell')[5]!;
    expect(cell).toHaveTextContent('▲+₹900▲+0.78%11 Oct');

    const infy = await screen.findByRole('row', { name: /^INFY\b/ });
    expect(within(within(infy).getAllByRole('gridcell')[5]!).getByRole('img')).toHaveAccessibleName(
      /No previous close yet/,
    );
  });

  it('shows previous close, price time in India time and the source in the drawer', async () => {
    renderPortfolio(`?holding=${holdingIds.reliance}`);

    const drawer = await screen.findByLabelText('Holding details');
    await within(drawer).findByText('Previous close', { selector: 'dt' });
    const fact = (label: string) => within(drawer).getByText(label, { selector: 'dt' }).nextElementSibling;
    expect(fact('Previous close')).toHaveTextContent('₹2,880.00');
    expect(fact('Day change')).toHaveTextContent('▲+₹900');
    expect(fact('Day change %')).toHaveTextContent('▲+0.78%');
    expect(fact('Price time (IST)')).toHaveTextContent('11 Oct 2026, 15:25 IST');
    expect(fact('Price source')).toHaveTextContent('Synced');
  });

  it('calls a market quote a Quote', async () => {
    server.use(
      http.get('/api/holdings', ({ request }) =>
        HttpResponse.json(
          searchHoldings(
            sampleHoldings.map((h) => ({ ...h, priceSource: 'quote' })),
            new URL(request.url).searchParams,
          ),
        ),
      ),
    );
    renderPortfolio(`?holding=${holdingIds.tcs}`);

    const drawer = await screen.findByLabelText('Holding details');
    expect((await within(drawer).findByText('Price source', { selector: 'dt' })).nextElementSibling).toHaveTextContent(
      'Quote',
    );
    expect(within(drawer).getByText('Day change', { selector: 'dt' }).nextElementSibling).toHaveTextContent('▼−₹200');
  });

  it('shows dashes in the drawer for a holding without a previous close', async () => {
    renderPortfolio(`?holding=${holdingIds.infy}`);

    const drawer = await screen.findByLabelText('Holding details');
    expect(
      (await within(drawer).findByText('Previous close', { selector: 'dt' })).nextElementSibling,
    ).toHaveTextContent('—');
    expect(within(drawer).getByText('Day change %', { selector: 'dt' }).nextElementSibling).toHaveTextContent('—');
  });
});

describe('Portfolio: the prices line', () => {
  const sessionSummary = (newest: string | null, overrides: Partial<HoldingsSummary> = {}) =>
    summaryWith({ pricesAsOf: { oldest: newest, newest }, ...overrides });

  it('says quotes are fresh during the session, with the interval of the service', async () => {
    setNow(sessionNow);
    server.use(sessionSummary(iso('10:33')));
    renderPortfolio();

    expect(await pricesLine('Prices as of 10:33 IST · quotes every 5 min')).toBeInTheDocument();
  });

  it('says the prices are delayed when the last quote is older than two intervals', async () => {
    setNow(sessionNow);
    server.use(sessionSummary(iso('10:00:02'.slice(0, 5))));
    renderPortfolio();

    expect(await pricesLine('Prices delayed — last quote 10:00 IST')).toBeInTheDocument();
  });

  it('says "from the last sync" for prices of an earlier day', async () => {
    setNow(sessionNow);
    server.use(sessionSummary(iso('15:25', '2026-10-10')));
    renderPortfolio();

    expect(await pricesLine('As of 10 Oct · from the last sync')).toBeInTheDocument();
  });

  it('gives the date and time of the prices outside the session', async () => {
    setNow('2026-10-13T16:00:00+05:30');
    server.use(sessionSummary(iso('10:00')));
    renderPortfolio();

    expect(await pricesLine('As of 13 Oct 10:00 IST · from the last quote')).toBeInTheDocument();
  });

  it('is not the session on a day that is not a trading day', async () => {
    setNow(sessionNow);
    server.use(
      sessionSummary(iso('10:33')),
      http.get('/api/services/timeline', () =>
        HttpResponse.json(buildTimeline({ tradingDay: false, marketOpen: null, marketClose: null })),
      ),
    );
    renderPortfolio();

    expect(await pricesLine(/^As of 13 Oct 10:33 IST · from the last (sync|quote)$/)).toBeInTheDocument();
  });

  it('says quotes are off when the service is paused, with a link to Services', async () => {
    setNow(sessionNow);
    server.use(sessionSummary(iso('10:00')), listWith(buildQuotesService({ enabled: false, status: 'paused' })));
    renderPortfolio();

    expect(await pricesLine('Quotes off — the service is paused · Prices as of 10:00 IST')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open Services' })).toHaveAttribute('href', '/services');
  });

  it.each([
    ['failed', 'The Data account’s token was refused.', 'the last run failed: The Data account’s token was refused.'],
    ['skipped', 'The Data account is not connected.', 'the last run was skipped: The Data account is not connected.'],
  ])('says quotes are off when the last run %s, with its reason', async (status, message, reason) => {
    setNow(sessionNow);
    server.use(
      sessionSummary(iso('10:00')),
      listWith(
        buildQuotesService({
          status: status === 'failed' ? 'failed' : 'healthy',
          lastRun: buildServiceRun({ id: serviceRunId(9), serviceId: 'holdings-quotes', status, message }),
        }),
      ),
    );
    renderPortfolio();

    expect(await pricesLine(`Quotes off — ${reason} · Prices as of 10:00 IST`)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open Services' })).toBeInTheDocument();
  });

  it('is not a live region and moves on once a minute', async () => {
    setNow(sessionNow);
    server.use(sessionSummary(iso('10:33')));
    renderPortfolio();

    const line = await pricesLine('Prices as of 10:33 IST · quotes every 5 min');
    expect(line.closest('[aria-live], [role="status"], [role="alert"]')).toBeNull();

    passMinutes(10);
    expect(await pricesLine('Prices delayed — last price 10:33 IST')).toBeInTheDocument();
  });

  it('says so when the quote service cannot be read, instead of guessing that quotes are on', async () => {
    setNow(sessionNow);
    server.use(
      sessionSummary(iso('10:33')),
      http.get('/api/services', () => new HttpResponse(null, { status: 500 })),
    );
    renderPortfolio();

    expect(await screen.findByText('The quote service status could not be read.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Refresh prices' })).toBeDisabled();
  });

  it('is left out when there are no open holdings', async () => {
    setNow(sessionNow);
    server.use(summaryWith({ openCount: 0, pricesAsOf: { oldest: null, newest: null } }));
    renderPortfolio();

    await screen.findByText('Holdings');
    expect(screen.queryByText(/^Prices as of|^As of|^Quotes off|^Prices delayed/)).not.toBeInTheDocument();
  });
});

describe('Portfolio: Refresh prices', () => {
  const quotesFake = (options: Parameters<typeof createServicesFake>[0] = {}) =>
    createServicesFake({ services: [buildQuotesService()], ...options });

  const countLoads = () => {
    const loads = { holdings: 0, summary: 0 };
    server.use(
      http.get('/api/holdings', ({ request }) => {
        loads.holdings += 1;
        return HttpResponse.json(searchHoldings(sampleHoldings, new URL(request.url).searchParams));
      }),
      http.get('/api/holdings/summary', () => {
        loads.summary += 1;
        return HttpResponse.json(buildSummary());
      }),
    );
    return loads;
  };

  const press = async () => {
    const button = await screen.findByRole('button', { name: 'Refresh prices' });
    await vi.waitFor(() => {
      expect(button).toBeEnabled();
    });
    await userEvent.click(button);
  };

  it('runs the service, shows it busy, then reads the figures again and toasts the run message', async () => {
    const fake = quotesFake({ runMessage: 'Quoted 41 of 43 holdings' });
    server.use(...fake.handlers);
    const loads = countLoads();
    renderPortfolio();
    await screen.findByRole('gridcell', { name: 'RELIANCE' });
    const before = { ...loads };

    await press();

    expect(await screen.findByRole('button', { name: 'Refreshing prices…' })).toBeDisabled();
    expect(fake.calls.runs).toEqual(['holdings-quotes']);
    expect(await screen.findByText('Quoted 41 of 43 holdings')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Refresh prices' })).toBeEnabled();
    await vi.waitFor(() => {
      expect(loads.holdings).toBeGreaterThan(before.holdings);
      expect(loads.summary).toBeGreaterThan(before.summary);
    });
  });

  it('explains a run that failed', async () => {
    const fake = quotesFake({ runOutcome: 'failed' });
    server.use(...fake.handlers);
    renderPortfolio();

    await press();

    expect(
      await screen.findByText('Prices could not be refreshed: Dhan refused the access token.'),
    ).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Refresh prices' })).toBeEnabled();
  });

  it('explains a run that was skipped', async () => {
    const fake = quotesFake({ runOutcome: 'skipped' });
    server.use(...fake.handlers);
    renderPortfolio();

    await press();

    expect(
      await screen.findByText('Prices were not refreshed: The Data account is not connected.'),
    ).toBeInTheDocument();
  });

  it('explains a 409 "already running" and follows the run that is going on', async () => {
    const fake = quotesFake({ runMessage: 'Quoted 41 of 43 holdings' });
    server.use(
      http.post('/api/services/holdings-quotes/run', () => {
        // somebody else started it a moment ago
        fake.startScheduledRun('holdings-quotes');
        return problemResponse(
          { title: 'Holdings quotes is already running.', code: 'services.already_running', status: 409 },
          409,
        );
      }),
      ...fake.handlers,
    );
    renderPortfolio();

    await press();

    expect(
      await screen.findByText('Prices are already being refreshed. This page updates when that run ends.'),
    ).toBeInTheDocument();
    expect(await screen.findByText('Quoted 41 of 43 holdings')).toBeInTheDocument();
  });

  it('says so when the request itself fails', async () => {
    server.use(
      http.post('/api/services/holdings-quotes/run', () =>
        problemResponse({ title: 'The services are not available.', code: 'services.down' }, 500),
      ),
    );
    renderPortfolio();

    await press();

    expect(await screen.findByText('The services are not available.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Refresh prices' })).toBeEnabled();
  });

  it('is disabled while the service runs, however it was started, and reads the figures when that ends', async () => {
    const fake = quotesFake();
    fake.startScheduledRun('holdings-quotes');
    server.use(...fake.handlers);
    const loads = countLoads();
    renderPortfolio();

    const busy = await screen.findByRole('button', { name: 'Refreshing prices…' });
    expect(busy).toBeDisabled();
    const before = { ...loads };

    expect(await screen.findByRole('button', { name: 'Refresh prices' })).toBeEnabled();
    await vi.waitFor(() => {
      expect(loads.holdings).toBeGreaterThan(before.holdings);
    });
  });
});

describe('Portfolio: reading the prices again while the market is open', () => {
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
      count: () => timers.size,
      fire: () => {
        act(() => {
          for (const callback of [...timers.values()]) callback();
        });
      },
    };
  }

  const countLoads = (summary: Partial<HoldingsSummary> = {}) => {
    const loads = { holdings: 0, summary: 0 };
    server.use(
      http.get('/api/holdings', ({ request }) => {
        loads.holdings += 1;
        return HttpResponse.json(searchHoldings(sampleHoldings, new URL(request.url).searchParams));
      }),
      http.get('/api/holdings/summary', () => {
        loads.summary += 1;
        return HttpResponse.json(buildSummary(summary));
      }),
    );
    return loads;
  };

  const loaded = async () => {
    await screen.findByRole('gridcell', { name: 'RELIANCE' });
    await screen.findByText('₹1,85,000');
    await vi.waitFor(() => {
      expect(screen.getByRole('button', { name: 'Refresh prices' })).toBeEnabled();
    });
  };

  it('reads the holdings and the totals again every minute while it is open and the page is visible', async () => {
    setNow(sessionNow);
    const timers = captureMinuteTimers();
    const loads = countLoads();
    renderPortfolio();
    await loaded();
    const before = { ...loads };

    expect(timers.count()).toBeGreaterThan(0);
    timers.fire();

    await vi.waitFor(() => {
      expect(loads.holdings).toBeGreaterThan(before.holdings);
      expect(loads.summary).toBeGreaterThan(before.summary);
    });
  });

  it('does not read them in a tab that is hidden', async () => {
    setNow(sessionNow);
    const timers = captureMinuteTimers();
    const loads = countLoads();
    renderPortfolio();
    await loaded();
    const before = { ...loads };

    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
    timers.fire();
    await new Promise((resolve) => setTimeout(resolve, 100));

    expect(loads).toEqual(before);
  });

  it('does not poll outside the session', async () => {
    setNow('2026-10-13T20:00:00+05:30');
    const timers = captureMinuteTimers();
    renderPortfolio();
    await loaded();

    expect(timers.count()).toBe(0);
  });

  it('does not poll on a day that is not a trading day', async () => {
    setNow(sessionNow);
    server.use(
      http.get('/api/services/timeline', () =>
        HttpResponse.json(buildTimeline({ tradingDay: false, marketOpen: null, marketClose: null })),
      ),
    );
    const timers = captureMinuteTimers();
    renderPortfolio();
    await loaded();

    expect(timers.count()).toBe(0);
  });

  it('reads once more when the session ends and then stops', async () => {
    setNow('2026-10-13T15:29:30+05:30');
    const timers = captureMinuteTimers();
    const loads = countLoads({ pricesAsOf: { oldest: iso('15:29'), newest: iso('15:29') } });
    renderPortfolio();
    await loaded();
    expect(timers.count()).toBeGreaterThan(0);
    expect(screen.getByText(/^Prices as of 15:29 IST/)).toBeInTheDocument();
    const before = { ...loads };

    passMinutes(2); // 15:31:30: the closing quote of 15:30 is in

    await vi.waitFor(() => {
      expect(loads.holdings).toBeGreaterThan(before.holdings);
      expect(timers.count()).toBe(0);
    });
    expect(await screen.findByText(/^As of 13 Oct 15:29 IST/)).toBeInTheDocument();
  });
});
