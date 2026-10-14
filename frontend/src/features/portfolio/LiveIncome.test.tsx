import { screen, waitFor, within } from '@testing-library/react';
import { captureIntervals, setNow } from '@/test/clock';
import { createHubFake, instrumentOf, priceOf, type HubFake } from '@/test/hubFake';
import { ids } from '@/test/fixtures';
import {
  isoAt,
  liveAccountIncome,
  liveHoldingsWithIncome,
  liveNow,
  serveLive,
  type LiveServer,
} from '@/test/liveFixtures';
import { writePrice } from '@/test/serverModel';
import { renderPortfolio } from './testing';

let hub: HubFake;
let api: LiveServer;

beforeEach(() => {
  hub = createHubFake();
  hub.install();
  api = serveLive(liveHoldingsWithIncome(), liveAccountIncome());
  setNow(liveNow);
});

/** The tile that carries a label, e.g. "Net total return". */
const tile = async (label: string) => {
  const card = (await screen.findByText(label, { selector: 'p' })).closest('.MuiCard-root');
  return within(card as HTMLElement);
};

async function following(search = '') {
  const view = renderPortfolio(search);
  await (search.includes('groupBy')
    ? screen.findByRole('table', { name: /^Holdings by/ })
    : screen.findByRole('row', { name: /^TCS\b/ }));
  await waitFor(() => {
    expect(hub.subscribes).not.toHaveLength(0);
  });
  await waitFor(() => {
    expect(api.loads.base).toBeGreaterThan(0);
  });
  return view;
}

describe('Portfolio: dividends and costs while the prices are live', () => {
  it('moves the net total return with the price and leaves the dividends, costs and net XIRR as read; a re-read gives the same', async () => {
    const timers = captureIntervals(300_000);
    await following();
    const net = await tile('Net total return');
    const before = net.getAllByText(/%$/).map((node) => node.textContent);
    expect(before).toHaveLength(2);
    expect((await tile('Dividends')).getByText('₹1,400')).toBeInTheDocument();
    expect((await tile('Costs')).getByText('₹575')).toBeInTheDocument();
    expect((await tile('Net XIRR')).getByText('+11.50%')).toBeInTheDocument();

    hub.push([priceOf(2, 3920, isoAt('10:35:42'))], isoAt('10:35:43'));

    await waitFor(() => {
      expect(net.getAllByText(/%$/).map((node) => node.textContent)).not.toEqual(before);
    });
    const live = net.getAllByText(/%$/).map((node) => node.textContent);
    // What does not depend on the price does not move; the net XIRR is the last read's, it is not worked out on a tick.
    expect((await tile('Dividends')).getByText('₹1,400')).toBeInTheDocument();
    expect((await tile('Costs')).getByText('₹575')).toBeInTheDocument();
    expect((await tile('Net XIRR')).getByText('+11.50%')).toBeInTheDocument();

    // The server now holds the pushed price; the next read (every 5 minutes while live) says the same.
    api.holdings = writePrice(api.holdings, instrumentOf(2), 392_000, Date.parse(isoAt('10:35:42')), 'live');
    const summaryLoads = api.loads.summary;
    timers.fire(300_000);
    await waitFor(() => {
      expect(api.loads.summary).toBeGreaterThan(summaryLoads);
    });
    await waitFor(() => {
      expect(screen.queryAllByRole('progressbar')).toHaveLength(0);
    });
    expect(net.getAllByText(/%$/).map((node) => node.textContent)).toEqual(live);
  });

  it('says that XIRR and net XIRR are worked out at each read while the net total return moves', async () => {
    await following();

    expect(
      await screen.findByText(
        'Net total return moves with the live prices; XIRR and net XIRR are worked out at each read.',
      ),
    ).toBeInTheDocument();
  });

  it('keeps the Unallocated row as read and does not ask for its members, while the group rows move', async () => {
    await following('?groupBy=sector');
    const table = await screen.findByRole('table', { name: 'Holdings by sector' });
    await waitFor(() => {
      expect(api.loads.base).toBeGreaterThan(1);
    });
    const unallocated = within(table).getByRole('row', { name: /^Unallocated/ });
    expect(unallocated).toHaveTextContent('₹200');
    const before = unallocated.textContent;

    hub.push([priceOf(2, 3920, isoAt('10:35:42'))], isoAt('10:35:43'));

    await waitFor(() => {
      expect(within(table).getAllByText('₹74,200').length).toBeGreaterThan(0);
    });
    expect(within(table).getByRole('row', { name: /^Unallocated/ }).textContent).toBe(before);
    expect(api.lists.some((params) => params.get('groupKey') === '~unallocated')).toBe(false);
  });

  it('keeps the dividends of an account group as read when its prices move', async () => {
    await following('?groupBy=account');
    const table = await screen.findByRole('table', { name: 'Holdings by account' });
    const group = (await within(table).findByText(ids.main)).closest('tr') as HTMLElement;
    // The account group holds its holdings' dividends and the account's own (₹1,200 and ₹200).
    expect(group).toHaveTextContent('₹1,400');

    hub.push([priceOf(2, 3920, isoAt('10:35:42'))], isoAt('10:35:43'));

    await waitFor(() => {
      expect(group).toHaveTextContent('₹74,200');
    });
    expect(group).toHaveTextContent('₹1,400');
  });
});
