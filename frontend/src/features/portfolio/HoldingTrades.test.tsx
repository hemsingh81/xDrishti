import { screen, waitFor, within, type queries, type BoundFunctions } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { holdingIds } from '@/test/fixtures';
import { pendingResponse, releasePending, releaseStep } from '@/test/gate';
import { server } from '@/test/server';
import { smallPage } from '@/test/smallPages';
import { buildRebuild, buildTrade, tradeIds } from '@/test/tradeFixtures';
import { renderPortfolio, serveTrades, warmTrades } from './testing';

// The list shows 50 trades a page in the app; here it pages by a few (the 50 is the constant `tradePageSize`).
vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
  (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
);

beforeAll(warmTrades, 60_000);

const buy = buildTrade();
const sell = buildTrade({
  id: tradeIds.second,
  side: 'sell',
  qty: 5,
  price: 1_650,
  charges: 0,
  tradeDate: '2026-09-20',
  tradeTime: '14:05:00',
  source: 'manual',
  batchId: null,
});
const voidedBuy = buildTrade({
  id: tradeIds.third,
  qty: 20,
  price: 1_400,
  tradeDate: '2024-06-10',
  voided: true,
  voidedAt: '2026-10-13T08:00:00Z',
  voidedBy: 'owner',
});

async function openInfy(trades = [buy, sell, voidedBuy]) {
  const fake = serveTrades({ trades });
  const view = renderPortfolio(`?holding=${holdingIds.infy}`);
  const drawer = within(await screen.findByLabelText('Holding details'));
  const section = within(await drawer.findByRole('region', { name: 'Trades' }));
  return { ...view, fake, drawer, section };
}

describe('Holding drawer: Trades section', () => {
  it('lists the trades newest first with date, side, quantity, price, charges and source', async () => {
    const { section } = await openInfy();

    const table = within(await section.findByRole('table', { name: 'Trades of INFY' }));
    expect(table.getAllByRole('columnheader').map((cell) => cell.textContent)).toEqual([
      'Date',
      'Side',
      'Qty',
      'Price',
      'Charges',
      'Source',
      'Action',
    ]);
    const first = within(table.getByRole('row', { name: /5 Oct 2026/ }));
    expect(first.getByText('Buy')).toBeInTheDocument();
    expect(first.getByText('10')).toBeInTheDocument();
    expect(first.getByText('₹1,750.00')).toBeInTheDocument();
    expect(first.getByText('₹12.50')).toBeInTheDocument();
    expect(first.getByText('CSV import')).toBeInTheDocument();
    const second = within(table.getByRole('row', { name: /20 Sept 2026/ }));
    expect(second.getByText('Sell')).toBeInTheDocument();
    expect(second.getByText(/14:05/)).toBeInTheDocument();
    expect(second.getByText('By hand')).toBeInTheDocument();
    expect(section.getByText(/Adding or voiding a trade does not change the lots/)).toBeInTheDocument();
  });

  it('shows a voided trade struck through with who voided it and when, and no Void button', async () => {
    const { section } = await openInfy();

    const table = within(await section.findByRole('table', { name: 'Trades of INFY' }));
    const row = within(table.getByRole('row', { name: /10 Jun 2024/ }));
    expect(row.getByText('Voided by owner on 13 Oct 2026')).toBeInTheDocument();
    expect(row.getByText('10 Jun 2024').closest('span')).toHaveStyle({ textDecoration: 'line-through' });
    expect(row.getByText('20')).toHaveStyle({ textDecoration: 'line-through' });
    expect(row.queryByRole('button', { name: /Void/ })).not.toBeInTheDocument();
    expect(table.getAllByRole('button', { name: /^Void the / })).toHaveLength(2);
  });

  it('says there are no trades yet', async () => {
    const { section } = await openInfy([]);

    expect(await section.findByText(/No trades for this holding yet/)).toBeInTheDocument();
    expect(section.queryByRole('table')).not.toBeInTheDocument();
  });

  it('shows progress while the trades load, and an error with Retry', async () => {
    serveTrades();
    let state: 'hold' | 'fail' | 'ok' = 'hold';
    server.use(
      http.get('/api/holdings/:id/trades', () => {
        if (state === 'hold') return pendingResponse();
        return state === 'fail'
          ? new HttpResponse(null, { status: 500 })
          : HttpResponse.json({ items: [buy], page: 1, pageSize: 50, total: 1 });
      }),
    );
    renderPortfolio(`?holding=${holdingIds.infy}`);
    const drawer = within(await screen.findByLabelText('Holding details'));
    expect(await drawer.findByRole('progressbar', { name: 'Loading trades' })).toBeInTheDocument();

    state = 'fail';
    await releasePending();
    expect(await drawer.findByText('The trades could not be loaded.')).toBeInTheDocument();
    state = 'ok';
    await userEvent.click(drawer.getByRole('button', { name: 'Retry' }));
    expect(await drawer.findByRole('table', { name: 'Trades of INFY' })).toBeInTheDocument();
  });

  it('shows more trades on request', async () => {
    const many = Array.from({ length: smallPage + 4 }, (_, n) =>
      buildTrade({ id: `00000000-0000-4000-8000-000000007${String(n + 10).padStart(3, '0')}`, qty: n + 1 }),
    );
    const { section } = await openInfy(many);

    const table = () => within(section.getByRole('table', { name: 'Trades of INFY' }));
    await section.findByRole('table', { name: 'Trades of INFY' });
    expect(table().getAllByRole('button', { name: /^Void/ })).toHaveLength(smallPage);
    expect(section.getByText(`Showing ${String(smallPage)} of ${String(many.length)}`)).toBeInTheDocument();
    await userEvent.click(section.getByRole('button', { name: 'Show more' }));
    await waitFor(() => {
      expect(table().getAllByRole('button', { name: /^Void/ })).toHaveLength(many.length);
    });
  });
});

describe('Holding drawer: Void', () => {
  const buyName = 'the buy of 10 INFY on 5 Oct 2026';
  const sellName = 'the sell of 5 INFY on 20 Sept 2026';

  type Table = BoundFunctions<typeof queries>;
  const askVoid = (table: Table, name = buyName) =>
    userEvent.click(table.getByRole('button', { name: `Void ${name}` }));
  const confirmVoid = (table: Table, name = buyName) =>
    userEvent.click(table.getByRole('button', { name: `Yes, void ${name}` }));

  it('asks before voiding, starting on No, and does nothing when the answer is No', async () => {
    const { section, fake } = await openInfy();
    const table = within(await section.findByRole('table', { name: 'Trades of INFY' }));

    await askVoid(table);

    const group = within(table.getByRole('group', { name: `Void ${buyName}?` }));
    expect(group.getByText('Void this trade?')).toBeInTheDocument();
    expect(group.getByRole('button', { name: `No, keep ${buyName}` })).toHaveFocus();
    expect(fake.calls.voided).toEqual([]);

    await userEvent.click(group.getByRole('button', { name: `No, keep ${buyName}` }));

    expect(table.getByRole('button', { name: `Void ${buyName}` })).toHaveFocus();
    expect(table.queryByText('Void this trade?')).not.toBeInTheDocument();
    expect(fake.calls.voided).toEqual([]);
  });

  it('strikes the trade out at once, keeps it so when the server agrees and moves focus to the section title', async () => {
    const { section, fake } = await openInfy();
    const table = within(await section.findByRole('table', { name: 'Trades of INFY' }));

    await askVoid(table);
    await confirmVoid(table);

    expect(
      await screen.findByText(/Voided the buy of 10 INFY on 5 Oct 2026\. Your lots are unchanged/),
    ).toBeInTheDocument();
    expect(fake.calls.voided).toEqual([tradeIds.first]);
    const row = within(table.getByRole('row', { name: /5 Oct 2026/ }));
    expect(await row.findByText('Voided by owner on 13 Oct 2026')).toBeInTheDocument();
    expect(row.queryByRole('button', { name: /^Void/ })).not.toBeInTheDocument();
    expect(section.getByRole('heading', { name: 'Trades' })).toHaveFocus();
  });

  it('writes the day it was voided in India time', async () => {
    const lateVoid = buildTrade({
      id: tradeIds.fourth,
      tradeDate: '2026-10-01',
      voided: true,
      voidedAt: '2026-10-13T20:00:00Z',
      voidedBy: 'owner',
    });
    const { section } = await openInfy([lateVoid]);

    expect(await section.findByText('Voided by owner on 14 Oct 2026')).toBeInTheDocument();
  });

  it('shows the trade voided while the server is thinking, and puts it back when the server refuses', async () => {
    const { section, fake } = await openInfy();
    const table = within(await section.findByRole('table', { name: 'Trades of INFY' }));
    fake.config.holdVoid = true;

    await askVoid(table);
    await confirmVoid(table);

    const row = () => within(table.getByRole('row', { name: /5 Oct 2026/ }));
    expect(await row().findByText(/^Voided on /)).toBeInTheDocument();
    expect(row().queryByRole('button', { name: /^Void/ })).not.toBeInTheDocument();

    // The held request is answered with an error (503): the trade comes back as it was.
    await releasePending();

    expect(await screen.findByText(/Could not void the buy of 10 INFY on 5 Oct 2026/)).toBeInTheDocument();
    expect(row().queryByText(/^Voided on /)).not.toBeInTheDocument();
    expect(row().getByRole('button', { name: `Void ${buyName}` })).toBeInTheDocument();
  });

  it('puts only the refused trade back when two voids are on their way and the first fails', async () => {
    const { section, fake } = await openInfy();
    const table = within(await section.findByRole('table', { name: 'Trades of INFY' }));
    fake.config.stepVoids = true;
    fake.config.failVoids = [tradeIds.first];

    await askVoid(table);
    await confirmVoid(table);
    await askVoid(table, sellName);
    await confirmVoid(table, sellName);
    const rowOf = (text: RegExp) => within(table.getByRole('row', { name: text }));
    expect(await rowOf(/\b5 Oct 2026/).findByText(/^Voided on /)).toBeInTheDocument();
    expect(await rowOf(/20 Sept 2026/).findByText(/^Voided on /)).toBeInTheDocument();

    await releaseStep(); // the first (the buy) is refused
    expect(await screen.findByText('Could not void that trade.')).toBeInTheDocument();

    // The buy is back; the sell, still on its way, stays struck out.
    expect(rowOf(/\b5 Oct 2026/).getByRole('button', { name: `Void ${buyName}` })).toBeInTheDocument();
    expect(rowOf(/20 Sept 2026/).getByText(/^Voided on /)).toBeInTheDocument();
    expect(rowOf(/20 Sept 2026/).queryByRole('button', { name: /^Void/ })).not.toBeInTheDocument();

    await releaseStep(); // the sell goes through
    expect(await rowOf(/20 Sept 2026/).findByText('Voided by owner on 13 Oct 2026')).toBeInTheDocument();
    expect(rowOf(/\b5 Oct 2026/).getByRole('button', { name: `Void ${buyName}` })).toBeInTheDocument();
  });

  it('puts the trade back and says why when the server says no', async () => {
    const { section, fake } = await openInfy();
    const table = within(await section.findByRole('table', { name: 'Trades of INFY' }));
    fake.config.voidProblem = {
      status: 404,
      body: { title: 'The trade does not exist.', code: 'portfolio.not_found' },
    };

    await askVoid(table);
    await confirmVoid(table);

    expect(await screen.findByText('The trade does not exist.')).toBeInTheDocument();
    await waitFor(() => {
      expect(table.getByRole('button', { name: `Void ${buyName}` })).toBeInTheDocument();
    });
  });
});

describe('Holding drawer: Add trade', () => {
  it('adds a trade for the holding without asking for the account or the security, and lists it', async () => {
    const { section, fake } = await openInfy([buy]);
    await section.findByRole('table', { name: 'Trades of INFY' });

    await userEvent.click(section.getByRole('button', { name: 'Add trade' }));

    const dialog = within(await screen.findByRole('dialog', { name: 'Add trade' }));
    expect(dialog.getByText('INFY · Infosys')).toBeInTheDocument();
    expect(dialog.queryByLabelText('Account')).not.toBeInTheDocument();
    expect(dialog.queryByRole('combobox', { name: 'Security' })).not.toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: 'Sell' }));
    const { fireEvent } = await import('@testing-library/react');
    fireEvent.change(dialog.getByLabelText('Trade date'), { target: { value: '2026-10-08' } });
    await userEvent.type(dialog.getByLabelText('Quantity'), '4');
    await userEvent.type(dialog.getByLabelText('Price per share (₹)'), '1710');
    await userEvent.click(dialog.getByRole('button', { name: 'Add trade' }));

    await dialog.findByRole('heading', { name: 'Trade added' });
    expect(fake.calls.added[0]).toMatchObject({ holdingId: holdingIds.infy, side: 'sell', quantity: 4, price: 1_710 });
    await userEvent.click(dialog.getByRole('button', { name: 'Done' }));
    await waitFor(() => {
      expect(section.getByRole('row', { name: /8 Oct 2026/ })).toBeInTheDocument();
    });
  });

  it('offers no Add trade until the holding’s own figures are known', async () => {
    serveTrades({ trades: [buy] });
    server.use(http.get('/api/holdings', () => HttpResponse.json({ items: [], page: 1, pageSize: 50, total: 0 })));
    renderPortfolio(`?holding=${holdingIds.infy}`);
    const drawer = within(await screen.findByLabelText('Holding details'));

    expect(await drawer.findByRole('button', { name: 'Rebuild lots' })).toBeInTheDocument();
    expect(drawer.queryByRole('button', { name: 'Add trade' })).not.toBeInTheDocument();
  });
});

describe('Holding drawer: Rebuild lots', () => {
  async function askRebuild(rebuild = buildRebuild()) {
    const view = await openInfy();
    view.fake.config.rebuild = rebuild;
    await userEvent.click(view.section.getByRole('button', { name: 'Rebuild lots' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Rebuild the lots of INFY' }));
    return { ...view, dialog };
  }

  it('shows the dry run first: status, lots and opening quantity before → after, realised profit', async () => {
    const { dialog, fake } = await askRebuild(
      buildRebuild({
        lotsBefore: 2,
        lotsAfter: 5,
        openingQty: { before: 25, after: 10 },
        realisedPnl: -300,
        adjustedFor: 1,
      }),
    );

    expect(await dialog.findByText('OK')).toBeInTheDocument();
    expect(dialog.getByText('Nothing has changed yet: this is what the rebuild would do.')).toBeInTheDocument();
    expect(dialog.getByText('2 → 5')).toBeInTheDocument();
    expect(dialog.getByText('25 → 10')).toBeInTheDocument();
    expect(dialog.getByText('−₹300')).toBeInTheDocument();
    expect(
      dialog.getByText(/Rebuilding replaces all the lots of the holding, including lots you dated by hand/),
    ).toBeInTheDocument();
    expect(dialog.getByText(/converted to today’s shares \(1 corporate action\)/)).toBeInTheDocument();
    expect(fake.calls.rebuilds).toEqual([{ holdingId: holdingIds.infy, dryRun: true }]);
    expect(dialog.getByRole('button', { name: 'Rebuild lots' })).toBeEnabled();
  });

  it('shows the cost of the open lots and why it can differ from the broker’s, or that it is approximated', async () => {
    const { dialog } = await askRebuild(
      buildRebuild({ openCostAfter: 38_766, brokerCost: 40_000, costDelta: -1_234, costNote: 'opening_cost_fallback' }),
    );

    expect(await dialog.findByText('Cost of the open lots after')).toBeInTheDocument();
    expect(dialog.getByText('₹38,766')).toBeInTheDocument();
    expect(
      dialog.getByText(
        /Cost basis from your trades is ₹1,234 lower than the broker’s cost \(₹40,000\)\. The lots built from your trades are costed first-in first-out/,
      ),
    ).toBeInTheDocument();
    expect(dialog.getByText(/Cost approximated: the opening lot’s cost could not be derived/)).toBeInTheDocument();
  });

  it('shows no cost remark when the cost agrees', async () => {
    const { dialog } = await askRebuild();

    expect(await dialog.findByText('Cost of the open lots after')).toBeInTheDocument();
    expect(dialog.queryByText(/Cost basis from your trades is/)).not.toBeInTheDocument();
  });

  it('explains a cost difference without an opening lot by the real buy prices, and shows the cost for an unchanged holding too', async () => {
    const { dialog } = await askRebuild(
      buildRebuild({
        status: 'unchanged',
        openingQty: { before: 0, after: 0 },
        realisedPnl: 0,
        openCostAfter: 41_000,
        brokerCost: 40_000,
        costDelta: 1_000,
      }),
    );

    expect(await dialog.findByText('Cost of the open lots')).toBeInTheDocument();
    expect(dialog.getByText('₹41,000')).toBeInTheDocument();
    expect(
      dialog.getByText(/₹1,000 higher than the broker’s cost \(₹40,000\)\. Your trades explain the whole holding/),
    ).toBeInTheDocument();
    expect(dialog.getByText(/real buy prices in the file/)).toBeInTheDocument();
  });

  it('applies on confirm, says what was done and closes', async () => {
    const { dialog, fake } = await askRebuild(buildRebuild({ lotsBefore: 2, lotsAfter: 5 }));
    await dialog.findByText('2 → 5');

    await userEvent.click(dialog.getByRole('button', { name: 'Rebuild lots' }));

    expect(await screen.findByText('Rebuilt the lots of INFY: 2 lots replaced by 5 lots.')).toBeInTheDocument();
    expect(fake.calls.rebuilds).toEqual([
      { holdingId: holdingIds.infy, dryRun: true },
      { holdingId: holdingIds.infy, dryRun: false },
    ]);
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Rebuild the lots of INFY' })).not.toBeInTheDocument();
    });
  });

  it('applies nothing on Cancel', async () => {
    const { dialog, fake } = await askRebuild();
    await dialog.findByText('OK');

    await userEvent.click(dialog.getByRole('button', { name: 'Cancel' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Rebuild the lots of INFY' })).not.toBeInTheDocument();
    });
    expect(fake.calls.rebuilds.every((call) => call.dryRun)).toBe(true);
  });

  it('needs a tick when hand-dated lots would be replaced and names the replaced lots', async () => {
    const { dialog } = await askRebuild(buildRebuild({ handDatedLotsReplaced: 2, syncLotsReplaced: 1 }));

    expect(await dialog.findByText('2 hand-dated lots and 1 lot from syncs will be replaced.')).toBeInTheDocument();
    const apply = dialog.getByRole('button', { name: 'Rebuild lots' });
    expect(apply).toBeDisabled();
    await userEvent.click(
      dialog.getByRole('checkbox', { name: 'I understand that 2 lots I dated by hand will be replaced.' }),
    );
    expect(apply).toBeEnabled();
  });

  it.each([
    ['unchanged', buildRebuild({ status: 'unchanged' }), 'Unchanged', /The lots already match the trades/],
    [
      'a mismatch',
      buildRebuild({ status: 'quantity_mismatch', reason: 'sell_exceeds_open', q: 25, net: 40 }),
      'Quantity mismatch',
      /A sell is larger than the shares held at that point.*Dhan shows 25 shares; the trades add up to a net 40/,
    ],
    [
      'an ambiguous holding',
      buildRebuild({ status: 'ambiguous' }),
      'Ambiguous',
      /More than one holding of the account matches/,
    ],
    ['a holding that is gone', buildRebuild({ status: 'no_holding' }), 'Not held', /does not hold this security now/],
  ])('shows %s with its reason and no way to apply it', async (_name, rebuild, label, text) => {
    const { dialog } = await askRebuild(rebuild);

    expect(await dialog.findByText(label)).toBeInTheDocument();
    expect(dialog.getByText(text)).toBeInTheDocument();
    expect(dialog.queryByRole('button', { name: 'Rebuild lots' })).not.toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Done' })).toBeInTheDocument();
  });

  it('shows an error with Retry when the dry run fails', async () => {
    const { fake, section } = await openInfy();
    fake.config.dryRunProblem = { status: 500, body: { title: 'Boom' } };

    await userEvent.click(section.getByRole('button', { name: 'Rebuild lots' }));

    const dialog = within(await screen.findByRole('dialog', { name: 'Rebuild the lots of INFY' }));
    expect(await dialog.findByText('Boom')).toBeInTheDocument();
    fake.config.dryRunProblem = null;
    await userEvent.click(dialog.getByRole('button', { name: 'Retry' }));
    expect(await dialog.findByText('1 → 3')).toBeInTheDocument();
  });

  it('keeps the dialog open with the reason when a sync is running, and applies on the next try', async () => {
    const { dialog, fake } = await askRebuild();
    await dialog.findByText('1 → 3');
    fake.config.applyProblem = { status: 409, body: { title: 'Sync running', code: 'portfolio.sync_running' } };

    await userEvent.click(dialog.getByRole('button', { name: 'Rebuild lots' }));

    expect(
      await dialog.findByText('A sync or import of this account is already running. Wait a moment for it to finish.'),
    ).toBeInTheDocument();
    fake.config.applyProblem = null;
    await userEvent.click(dialog.getByRole('button', { name: 'Rebuild lots' }));
    expect(await screen.findByText(/Rebuilt the lots of INFY/)).toBeInTheDocument();
  });

  it('changes nothing and says so when the holding changed since the dry run', async () => {
    const { dialog } = await askRebuild();
    await dialog.findByText('1 → 3');
    server.use(
      http.post('/api/holdings/:id/rebuild-lots', () =>
        HttpResponse.json({
          dryRun: false,
          applied: false,
          rebuild: buildRebuild({ status: 'quantity_mismatch', reason: 'negative_opening', q: 30, net: 40 }),
        }),
      ),
    );

    await userEvent.click(dialog.getByRole('button', { name: 'Rebuild lots' }));

    expect(
      await dialog.findByText(/The holding changed since you looked; the lots were not changed/),
    ).toBeInTheDocument();
    expect(dialog.getByText('Quantity mismatch')).toBeInTheDocument();
    expect(dialog.queryByRole('button', { name: 'Rebuild lots' })).not.toBeInTheDocument();
  });
});
