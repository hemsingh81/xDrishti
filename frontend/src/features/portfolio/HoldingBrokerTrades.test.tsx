import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildBrokerCounts, buildBrokerKind, buildBrokerRun, buildBrokerStatus } from '@/test/brokerHistoryFixtures';
import { holdingIds } from '@/test/fixtures';
import { server } from '@/test/server';
import { buildTrade, tradeIds } from '@/test/tradeFixtures';
import { renderPortfolio, serveTrades, warmTrades } from './testing';

beforeAll(warmTrades, 60_000);

const csvBuy = buildTrade();
const dhanSell = buildTrade({
  id: tradeIds.second,
  side: 'sell',
  qty: 5,
  price: 1_650,
  charges: 0,
  tradeDate: '2026-10-09',
  source: 'broker',
  batchId: null,
});

async function openInfy(trades = [csvBuy, dhanSell]) {
  serveTrades({ trades });
  renderPortfolio(`?holding=${holdingIds.infy}`);
  const drawer = within(await screen.findByLabelText('Holding details'));
  return within(await drawer.findByRole('region', { name: 'Trades' }));
}

const needsReviewStatus = (symbol: string) =>
  buildBrokerStatus({
    holdingsNeedingReview: 1,
    needsReviewSymbols: [symbol],
    trades: buildBrokerKind({
      lastRun: buildBrokerRun({
        counts: buildBrokerCounts({ needsReview: 1, notRebuilt: [{ symbol, reason: 'needs_review' }] }),
      }),
    }),
  });

describe('Holding drawer: trades read from Dhan', () => {
  it('marks a trade read from Dhan and explains that a voided one comes back', async () => {
    const section = await openInfy();

    const table = within(await section.findByRole('table', { name: 'Trades of INFY' }));
    const dhan = within(table.getByRole('row', { name: /9 Oct 2026/ }));
    expect(dhan.getByText('Dhan')).toBeInTheDocument();
    expect(within(table.getByRole('row', { name: /5 Oct 2026/ })).queryByText('Dhan')).not.toBeInTheDocument();
    expect(
      section.getByText(/A Dhan trade you void is stored again the next time its day is read\./),
    ).toBeInTheDocument();
  });

  it('does not talk about Dhan trades when the holding has none', async () => {
    const section = await openInfy([csvBuy]);

    await section.findByRole('table', { name: 'Trades of INFY' });
    expect(section.queryByText(/A Dhan trade you void/)).not.toBeInTheDocument();
    expect(section.queryByText(/left the lots of INFY alone/)).not.toBeInTheDocument();
  });

  it('points at the preview-then-rebuild when the last read left the holding for review', async () => {
    server.use(
      http.get('/api/accounts/:id/sync/broker-history/status', () => HttpResponse.json(needsReviewStatus('INFY'))),
    );
    const section = await openInfy();

    expect(
      await section.findByText(/left the lots of INFY alone, because a lot has a date you set/),
    ).toBeInTheDocument();
    await userEvent.click(section.getByRole('button', { name: 'Review the rebuild' }));

    const dialog = await screen.findByRole('dialog', { name: /Rebuild/ });
    expect(dialog).toBeInTheDocument();
  });

  it('says nothing when the holding left for review is another one', async () => {
    let statusLoads = 0;
    server.use(
      http.get('/api/accounts/:id/sync/broker-history/status', () => {
        statusLoads += 1;
        return HttpResponse.json(needsReviewStatus('TCS'));
      }),
    );
    const section = await openInfy();

    await section.findByRole('table', { name: 'Trades of INFY' });
    await vi.waitFor(() => {
      expect(statusLoads).toBeGreaterThan(0);
    });
    expect(section.queryByRole('button', { name: 'Review the rebuild' })).not.toBeInTheDocument();
  });
});
