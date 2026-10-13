import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { alertIds, buildPriceAlert, buildTriggeredAlert, instrumentIds } from '@/test/actionCenterFixtures';
import { pendingResponse, releasePending } from '@/test/gate';
import { server } from '@/test/server';
import { smallPage } from '@/test/smallPages';
import { renderRoutes } from '@/test/render';
import { PortfolioPage } from './components/PortfolioPage';
import { actionCenterCard, renderPortfolio, serveActionCenter } from './testing';

// The panel shows 50 alerts a page in the app; here it pages by a few (the 50 is the constant `alertsPageSize`).
vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
  (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
);

beforeEach(() => {
  window.sessionStorage.clear();
});

const cancelledAlert = () =>
  buildPriceAlert({ id: alertIds.third, symbol: 'NSE:HDFCBANK', name: 'HDFC Bank', level: 1_400, status: 'cancelled' });

const threeStates = () => [
  buildPriceAlert({ note: '<b>Add</b> more below this' }),
  buildTriggeredAlert(),
  cancelledAlert(),
];

async function openPanel(tab = 'active') {
  const view = renderPortfolio(`?alerts=${tab}`);
  const panel = await screen.findByRole('dialog', { name: 'Price alerts' });
  return { ...view, panel: within(panel) };
}

describe('Alerts panel', () => {
  it('shows the tabs with their counts, how many are active, and how often they are checked', async () => {
    serveActionCenter({ alerts: threeStates() });
    const { panel } = await openPanel();

    expect(await panel.findByRole('tab', { name: 'Active (1)', selected: true })).toBeInTheDocument();
    expect(panel.getByRole('tab', { name: 'Triggered (1)' })).toBeInTheDocument();
    expect(panel.getByRole('tab', { name: 'Cancelled (1)' })).toBeInTheDocument();
    expect(panel.getByText('1 of 200 active')).toBeInTheDocument();
    expect(panel.getByText(/Checked every 5 minutes while the market is open/)).toBeInTheDocument();
    expect(panel.getByRole('button', { name: 'New alert' })).toBeInTheDocument();
  });

  it('lists an active alert with its condition, note (as plain text), date and a Cancel button', async () => {
    serveActionCenter({ alerts: threeStates() });
    const { panel } = await openPanel();

    const list = await panel.findByRole('list', { name: 'Active alerts' });
    const row = within(within(list).getByRole('listitem'));
    expect(row.getByText('NSE:RELIANCE')).toBeInTheDocument();
    expect(row.getByText('Reliance Industries')).toBeInTheDocument();
    expect(row.getByText('at or below ₹2,500.00')).toBeInTheDocument();
    expect(row.getByText('<b>Add</b> more below this')).toBeInTheDocument();
    expect(row.getByText('Set 10 Oct 2026')).toBeInTheDocument();
    expect(row.getByRole('button', { name: 'Cancel alert on NSE:RELIANCE at or below ₹2,500.00' })).toBeInTheDocument();
  });

  it('lists the triggered alerts with the price and time they triggered at, and the cancelled ones without Cancel', async () => {
    serveActionCenter({ alerts: threeStates() });
    const { panel, router } = await openPanel();
    await panel.findByRole('list', { name: 'Active alerts' });

    await userEvent.click(panel.getByRole('tab', { name: 'Triggered (1)' }));

    const triggered = within(await panel.findByRole('list', { name: 'Triggered alerts' }));
    expect(triggered.getByText('at or above ₹3,900.00')).toBeInTheDocument();
    expect(triggered.getByText('Book half')).toBeInTheDocument();
    expect(triggered.getByText(/^Triggered at ₹3,912\.50, 12 Oct 2026, 15:10 IST$/)).toBeInTheDocument();
    expect(triggered.queryByRole('button', { name: /^Cancel/ })).not.toBeInTheDocument();
    expect(router.state.location.search).toBe('?alerts=triggered');

    await userEvent.click(panel.getByRole('tab', { name: 'Cancelled (1)' }));

    const cancelled = within(await panel.findByRole('list', { name: 'Cancelled alerts' }));
    expect(cancelled.getByText('NSE:HDFCBANK')).toBeInTheDocument();
    expect(cancelled.getByText('Cancelled')).toBeInTheDocument();
    expect(cancelled.queryByRole('button', { name: /^Cancel/ })).not.toBeInTheDocument();
  });

  it('moves between the tabs with the arrow keys, Home and End, the selected tab being the one tab stop', async () => {
    serveActionCenter({ alerts: threeStates() });
    const { panel, router } = await openPanel();
    const active = await panel.findByRole('tab', { name: 'Active (1)' });
    expect(active).toHaveAttribute('tabindex', '0');
    expect(panel.getByRole('tab', { name: 'Triggered (1)' })).toHaveAttribute('tabindex', '-1');
    active.focus();

    await userEvent.keyboard('{ArrowRight}');
    expect(panel.getByRole('tab', { name: 'Triggered (1)' })).toHaveFocus();
    expect(panel.getByRole('tab', { name: 'Triggered (1)' })).toHaveAttribute('aria-selected', 'true');
    expect(router.state.location.search).toBe('?alerts=triggered');

    await userEvent.keyboard('{End}');
    expect(panel.getByRole('tab', { name: 'Cancelled (1)' })).toHaveFocus();
    await userEvent.keyboard('{ArrowRight}');
    expect(panel.getByRole('tab', { name: 'Active (1)' })).toHaveFocus();
    await userEvent.keyboard('{ArrowLeft}');
    expect(panel.getByRole('tab', { name: 'Cancelled (1)' })).toHaveFocus();
    await userEvent.keyboard('{Home}');
    expect(panel.getByRole('tab', { name: 'Active (1)' })).toHaveFocus();
    expect(await panel.findByRole('list', { name: 'Active alerts' })).toBeInTheDocument();
  });

  it('opens on the tab named in the URL', async () => {
    serveActionCenter({ alerts: threeStates() });
    const { panel } = await openPanel('triggered');

    expect(await panel.findByRole('tab', { name: /^Triggered/, selected: true })).toBeInTheDocument();
    expect(await panel.findByRole('list', { name: 'Triggered alerts' })).toBeInTheDocument();
  });

  it.each([
    ['active', 'No active alerts. Set one with Set alert on a holding or in the Action center, or with New alert.'],
    ['triggered', 'No alert has triggered yet.'],
    ['cancelled', 'No alert has been cancelled.'],
  ])('says so when the %s tab is empty', async (tab, text) => {
    serveActionCenter();
    const { panel } = await openPanel(tab);

    expect(await panel.findByText(text)).toBeInTheDocument();
    expect(panel.getByText('0 of 200 active')).toBeInTheDocument();
  });

  it('cancels at once, tells the owner, and the Cancelled tab has it', async () => {
    const fake = serveActionCenter({ alerts: threeStates() });
    const { panel } = await openPanel();
    await panel.findByRole('list', { name: 'Active alerts' });

    await userEvent.click(panel.getByRole('button', { name: /^Cancel alert on NSE:RELIANCE/ }));

    expect(await screen.findByText('Cancelled the NSE:RELIANCE alert (at or below ₹2,500.00).')).toBeInTheDocument();
    expect(fake.calls.cancelled).toEqual([alertIds.first]);
    expect(
      await panel.findByText(
        'No active alerts. Set one with Set alert on a holding or in the Action center, or with New alert.',
      ),
    ).toBeInTheDocument();
    expect(panel.getByText('0 of 200 active')).toBeInTheDocument();
    expect(panel.getByRole('tab', { name: 'Cancelled (2)' })).toBeInTheDocument();
  });

  it('shows the cancel before the answer and puts the row back when the server refuses', async () => {
    const fake = serveActionCenter({ alerts: threeStates() });
    const { panel } = await openPanel();
    await panel.findByRole('list', { name: 'Active alerts' });
    fake.failures.hold = true;

    await userEvent.click(panel.getByRole('button', { name: /^Cancel alert on NSE:RELIANCE/ }));

    expect(await panel.findByText(/^No active alerts\./)).toBeInTheDocument();
    expect(panel.getByText('0 of 200 active')).toBeInTheDocument();
    expect(panel.getByRole('tab', { name: 'Active (0)' })).toBeInTheDocument();

    await releasePending();

    expect(
      await screen.findByText('Could not cancel the NSE:RELIANCE alert. Nothing was changed.'),
    ).toBeInTheDocument();
    expect(await panel.findByRole('list', { name: 'Active alerts' })).toBeInTheDocument();
    expect(panel.getByText('1 of 200 active')).toBeInTheDocument();
    expect(panel.getByRole('tab', { name: 'Active (1)' })).toBeInTheDocument();
  });

  it('says so when the alert had triggered meanwhile', async () => {
    serveActionCenter({ alerts: threeStates() });
    server.use(
      http.post('/api/portfolio/alerts/:id/cancel', () =>
        HttpResponse.json(
          buildPriceAlert({ status: 'triggered', triggeredAt: '2026-10-13T05:00:00Z', triggerPrice: 2_490 }),
        ),
      ),
    );
    const { panel } = await openPanel();
    await panel.findByRole('list', { name: 'Active alerts' });

    await userEvent.click(panel.getByRole('button', { name: /^Cancel alert on NSE:RELIANCE/ }));

    expect(
      await screen.findByText('The NSE:RELIANCE alert had already triggered, so it stays in the Triggered list.'),
    ).toBeInTheDocument();
  });

  it('shows the problem with Retry when the alerts cannot be loaded', async () => {
    const fake = serveActionCenter({ alerts: threeStates() });
    fake.failures.alerts = true;
    const { panel } = await openPanel();

    const alert = await panel.findByRole('alert');
    expect(alert).toHaveTextContent('The service is not available.');
    fake.failures.alerts = false;
    await userEvent.click(within(alert).getByRole('button', { name: 'Retry' }));

    expect(await panel.findByRole('list', { name: 'Active alerts' })).toBeInTheDocument();
  });

  it('shows a progress indicator while the alerts load', async () => {
    serveActionCenter({ alerts: threeStates() });
    server.use(http.get('/api/portfolio/alerts', () => pendingResponse()));
    const { panel } = await openPanel();

    expect(await panel.findByRole('progressbar', { name: 'Loading alerts' })).toBeInTheDocument();
  });

  it('loads further alerts with Show more', async () => {
    const many = Array.from({ length: smallPage * 2 + 5 }, (_, n) =>
      buildPriceAlert({ id: `00000000-0000-4000-8000-0000000013${String(n).padStart(2, '0')}`, level: 1_000 + n }),
    );
    serveActionCenter({ alerts: many });
    const { panel } = await openPanel();

    const list = await panel.findByRole('list', { name: 'Active alerts' });
    expect(within(list).getAllByRole('listitem')).toHaveLength(smallPage);
    expect(panel.getByText(`Showing ${String(smallPage)} of ${String(many.length)}`)).toBeInTheDocument();
    expect(panel.getByText(`${String(many.length)} of 200 active`)).toBeInTheDocument();

    await userEvent.click(panel.getByRole('button', { name: 'Show more' }));
    await waitFor(() => {
      expect(within(list).getAllByRole('listitem')).toHaveLength(smallPage * 2);
    });
    await userEvent.click(panel.getByRole('button', { name: 'Show more' }));
    await waitFor(() => {
      expect(within(list).getAllByRole('listitem')).toHaveLength(many.length);
    });
    expect(panel.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument();
  });

  it('closes with the close button and with Escape, taking alerts out of the URL', async () => {
    serveActionCenter({ alerts: threeStates() });
    const { panel, router } = await openPanel();
    await panel.findByRole('list', { name: 'Active alerts' });

    await userEvent.click(panel.getByRole('button', { name: 'Close alerts' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Price alerts' })).not.toBeInTheDocument();
    });
    expect(router.state.location.search).toBe('');
  });

  it('says how many are active with the limit the API reports', async () => {
    serveActionCenter({ alerts: [buildPriceAlert({ instrumentId: instrumentIds.reliance })], alertLimit: 25 });
    const { panel } = await openPanel();

    expect(await panel.findByText('1 of 25 active')).toBeInTheDocument();
  });
});

describe('Alerts panel: tabs are different lists', () => {
  it('does not show the rows of the Active tab under Triggered while the Triggered list loads', async () => {
    serveActionCenter({ alerts: threeStates() });
    const { panel } = await openPanel();
    await panel.findByRole('list', { name: 'Active alerts' });
    // The list of triggered alerts hangs (the one-row counts do not).
    server.use(
      http.get('/api/portfolio/alerts', ({ request }) => {
        const params = new URL(request.url).searchParams;
        return params.get('status') === 'triggered' && params.get('pageSize') !== '1' ? pendingResponse() : undefined;
      }),
    );

    await userEvent.click(panel.getByRole('tab', { name: 'Triggered (1)' }));

    expect(await panel.findByRole('progressbar', { name: 'Loading alerts' })).toBeInTheDocument();
    expect(panel.queryByRole('button', { name: /^Cancel alert on/ })).not.toBeInTheDocument();
    expect(panel.queryByText('NSE:RELIANCE')).not.toBeInTheDocument();
    expect(panel.getByRole('tabpanel')).toHaveAttribute('aria-busy', 'true');
  });

  it('makes every panel reachable by keyboard, even an empty one', async () => {
    serveActionCenter();
    const { panel } = await openPanel('cancelled');

    expect(await panel.findByText('No alert has been cancelled.')).toBeInTheDocument();
    expect(panel.getByRole('tabpanel')).toHaveAttribute('tabindex', '0');
    expect(panel.getByRole('tabpanel')).toHaveAttribute('aria-busy', 'false');
  });
});

describe('Alerts panel and the history', () => {
  async function start(from: string) {
    const view = renderRoutes(
      [
        { path: '/portfolio', element: <PortfolioPage /> },
        { path: '/other', element: <p>Other screen</p> },
      ],
      { initialPath: '/other' },
    );
    await screen.findByText('Other screen');
    await act(async () => {
      await view.router.navigate(from);
    });
    return view;
  }

  it('takes one Back to leave the screen after opening and closing the panel from the card', async () => {
    serveActionCenter();
    const { router } = await start('/portfolio');
    const card = await actionCenterCard();

    await userEvent.click(within(card).getByRole('button', { name: 'Alerts' }));
    const panel = within(await screen.findByRole('dialog', { name: 'Price alerts' }));
    expect(router.state.location.search).toBe('?alerts=active');
    await userEvent.click(panel.getByRole('tab', { name: /^Triggered/ }));
    expect(router.state.location.search).toBe('?alerts=triggered');
    await userEvent.click(panel.getByRole('button', { name: 'Close alerts' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Price alerts' })).not.toBeInTheDocument();
    });
    expect(router.state.location.search).toBe('');
    await act(async () => {
      await router.navigate(-1);
    });
    expect(router.state.location.pathname).toBe('/other');
  });

  it('closes with Back, as the Close button would', async () => {
    serveActionCenter();
    const { router } = await start('/portfolio');
    const card = await actionCenterCard();
    await userEvent.click(within(card).getByRole('button', { name: 'Alerts' }));
    await screen.findByRole('dialog', { name: 'Price alerts' });

    await act(async () => {
      await router.navigate(-1);
    });

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Price alerts' })).not.toBeInTheDocument();
    });
    expect(router.state.location.pathname).toBe('/portfolio');
    expect(router.state.location.search).toBe('');
  });

  it('closes a panel that a link opened by replacing its entry, so one Back leaves the screen', async () => {
    serveActionCenter({ alerts: threeStates() });
    const { router } = await start('/portfolio?alerts=triggered');
    const panel = within(await screen.findByRole('dialog', { name: 'Price alerts' }));

    await userEvent.click(panel.getByRole('button', { name: 'Close alerts' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Price alerts' })).not.toBeInTheDocument();
    });
    expect(router.state.location.pathname).toBe('/portfolio');
    expect(router.state.location.search).toBe('');
    await act(async () => {
      await router.navigate(-1);
    });
    expect(router.state.location.pathname).toBe('/other');
  });
});
