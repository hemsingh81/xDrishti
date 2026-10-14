import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { IncomeView } from '@/shared/api';
import { buildIncome, summaryWithIncome } from '@/test/dividendFixtures';
import { buildAccountDetail, buildConnection, buildSummary, summaryOf, withConnection } from '@/test/fixtures';
import { server } from '@/test/server';
import { noHoldingsHandlers, renderPortfolio, twoPortfolioAccounts } from './testing';

function summaryHas(income: IncomeView | null) {
  server.use(http.get('/api/holdings/summary', () => HttpResponse.json(summaryWithIncome(buildSummary(), income))));
}

/** The tile with this label: its card. */
const tile = async (label: string) => {
  const heading = await screen.findByText(label, { selector: 'p' });
  const card = heading.closest('.MuiCard-root');
  if (!card) throw new Error(`No tile ${label}`);
  return within(card as HTMLElement);
};

const region = () => screen.findByRole('region', { name: 'Dividends and costs' });

describe('Summary tiles: nothing entered and nothing read', () => {
  it('keeps the price-return tiles and invites to add a dividend and read the ledger, instead of showing zeros', async () => {
    renderPortfolio();

    const invite = within(await region());
    expect(invite.getByRole('heading', { name: 'Dividends and costs are not in the figures yet' })).toBeInTheDocument();
    expect(invite.getByRole('button', { name: 'Add a dividend' })).toBeInTheDocument();
    expect(invite.getByRole('button', { name: 'Read the ledger' })).toBeInTheDocument();
    // The gross figures are as they were, and no tile claims there were no dividends or costs.
    expect(await screen.findByText('₹1,85,000')).toBeInTheDocument();
    expect(screen.getByText('+11.30%')).toBeInTheDocument();
    expect(screen.queryByText('Net XIRR')).not.toBeInTheDocument();
    expect(screen.queryByText('Net total return')).not.toBeInTheDocument();
    expect(screen.queryByText('Costs')).not.toBeInTheDocument();
    expect(
      screen.getByText(
        'Dividends and charges are not recorded yet, so these are price returns. Corporate actions are not included.',
      ),
    ).toBeInTheDocument();
  });

  it('shows no invitation when there are no holdings at all', async () => {
    server.use(...noHoldingsHandlers());
    renderPortfolio();

    expect(await screen.findByText('Invested')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Dividends and costs' })).not.toBeInTheDocument();
  });

  it('opens the Cash & dividends panel on the Add dividend form from "Add a dividend"', async () => {
    const { router } = renderPortfolio();

    await userEvent.click(within(await region()).getByRole('button', { name: 'Add a dividend' }));

    expect(await screen.findByRole('dialog', { name: 'Add dividend' })).toBeInTheDocument();
    // The panel is under the form (a modal hides it from assistive technology while the form is open).
    expect(screen.getByRole('dialog', { name: 'Cash & dividends', hidden: true })).toBeInTheDocument();
    expect(router.state.location.search).toBe('?cash=add');
  });

  it('takes the keyboard to Sync trades & ledger from "Read the ledger"', async () => {
    renderPortfolio();

    await userEvent.click(within(await region()).getByRole('button', { name: 'Read the ledger' }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Sync trades & ledger' })).toHaveFocus();
    });
  });

  it('takes the keyboard to the Account picker from "Read the ledger" when all accounts are shown: a read belongs to one account', async () => {
    server.use(twoPortfolioAccounts());
    renderPortfolio();

    await userEvent.click(within(await region()).getByRole('button', { name: 'Read the ledger' }));

    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: 'Account' })).toHaveFocus();
    });
    // Why: the bar says what to do, and has no Sync trades & ledger button to focus.
    expect(screen.getByText('Pick an account above to sync it.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Sync trades & ledger' })).not.toBeInTheDocument();
  });

  it('never focuses a disabled button from "Read the ledger": with the account not connected the sync bar takes the focus', async () => {
    const expired = withConnection(buildAccountDetail(), buildConnection({ status: 'expired' }));
    server.use(http.get('/api/accounts', () => HttpResponse.json([summaryOf(expired)])));
    renderPortfolio();
    const sync = await screen.findByRole('button', { name: 'Sync trades & ledger' });
    expect(sync).toBeDisabled();

    await userEvent.click(within(await region()).getByRole('button', { name: 'Read the ledger' }));

    await waitFor(() => {
      expect(document.activeElement).toContainElement(sync);
    });
    expect(sync).not.toHaveFocus();
    // The reason is on screen beside it.
    expect(screen.getByRole('link', { name: /Connect/i })).toBeInTheDocument();
  });
});

describe('Summary tiles: dividends, costs and the net figures', () => {
  it('adds Dividends, Costs, Net total return and Net XIRR with the day each is known from, and keeps the gross tiles', async () => {
    summaryHas(buildIncome({ unallocated: 200 }));
    renderPortfolio();

    const dividends = await tile('Dividends');
    expect(dividends.getByText('₹1,200')).toBeInTheDocument();
    expect(dividends.getByText('entered from 5 Dec 2025 · ₹200 unallocated')).toBeInTheDocument();
    const costs = await tile('Costs');
    expect(costs.getByText('₹150')).toBeInTheDocument();
    expect(costs.getByText('trade charges from 2 Jan 2025')).toBeInTheDocument();
    expect(costs.queryByText(/of which account costs/)).not.toBeInTheDocument();
    const net = await tile('Net total return');
    expect(net.getByText('+17.05%')).toHaveTextContent('▲+17.05%');
    expect(net.getByText('+17.20%')).toBeInTheDocument();
    expect(net.getByText(/counted from 5 Dec 2025/)).toBeInTheDocument();
    const xirr = await tile('Net XIRR');
    expect(xirr.getByText('+13.80%')).toHaveTextContent('▲+13.80%');

    // The gross tiles are untouched.
    expect((await tile('XIRR')).getByText('+11.30%')).toBeInTheDocument();
    expect((await tile('Unrealised P&L')).getByText('+9.95%')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add a dividend' })).not.toBeInTheDocument();
    expect(
      screen.getByText(
        'Net figures count dividends and costs only from the dates on their tiles. Corporate actions are not included.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText(/complete/i)).not.toBeInTheDocument();
  });

  it('shows the two cost sources on their own days and the account costs as part of the total', async () => {
    summaryHas(
      buildIncome({
        charges: 270,
        ledgerCharges: 120,
        tradeChargesFrom: '2025-01-02',
        ledgerChargesFrom: '2026-01-05',
        chargesFrom: '2026-01-05',
        coverageFrom: '2026-01-05',
      }),
    );
    renderPortfolio();

    const costs = await tile('Costs');
    expect(costs.getByText('₹270')).toBeInTheDocument();
    expect(costs.getByText('of which account costs ₹120')).toBeInTheDocument();
    expect(costs.getByText('trade charges from 2 Jan 2025 · account costs from 5 Jan 2026')).toBeInTheDocument();
    // The net tiles are counted from the later start of the sources, never earlier than any of them.
    expect((await tile('Net total return')).getByText(/counted from 5 Jan 2026/)).toBeInTheDocument();
    expect(
      (await tile('Net XIRR')).getByText('with dividends and costs · counted from 5 Jan 2026'),
    ).toBeInTheDocument();
  });

  it('explains on the net total return tile what the percentage is measured against', async () => {
    summaryHas(buildIncome());
    renderPortfolio();

    const net = await tile('Net total return');

    await userEvent.hover(net.getByRole('button', { name: 'How the total return is worked out' }));
    expect(await screen.findByRole('tooltip')).toHaveTextContent(
      'Realised profit is added to the numerator; the base is the cost of what you still hold.',
    );
  });

  it('with dividends only says that no costs were read, and the net return is the total return', async () => {
    summaryHas(
      buildIncome({
        charges: 0,
        chargesFrom: null,
        tradeChargesFrom: null,
        coverageFrom: '2025-12-05',
        netTotalReturnPct: 17.2,
      }),
    );
    renderPortfolio();

    const costs = await tile('Costs');
    expect(costs.getByText('₹0')).toBeInTheDocument();
    expect(costs.getByText('none read yet')).toBeInTheDocument();
    expect((await tile('Dividends')).getByText('₹1,200')).toBeInTheDocument();
    expect((await tile('Net total return')).getByText(/counted from 5 Dec 2025/)).toBeInTheDocument();
    expect((await tile('Net total return')).getAllByText('+17.20%')).toHaveLength(2);
  });

  it('with charges only says that no dividends were entered, and the net return is below the gross one', async () => {
    summaryHas(
      buildIncome({
        dividends: 0,
        dividendsFrom: null,
        totalReturnPct: 9.95,
        netTotalReturnPct: 9.7,
        coverageFrom: '2025-01-02',
        netXirr: 10.9,
      }),
    );
    renderPortfolio();

    const dividends = await tile('Dividends');
    expect(dividends.getByText('₹0')).toBeInTheDocument();
    expect(dividends.getByText('none entered yet')).toBeInTheDocument();
    expect((await tile('Costs')).getByText('₹150')).toBeInTheDocument();
    const net = await tile('Net total return');
    expect(net.getByText('+9.70%')).toBeInTheDocument();
    expect(net.getByText('+9.95%')).toBeInTheDocument();
    expect((await tile('Net XIRR')).getByText('+10.90%')).toBeInTheDocument();
  });

  it('says in words why there is no net XIRR, as for the gross XIRR, and why there is no net return', async () => {
    summaryHas(
      buildIncome({ netXirr: null, netXirrState: 'insufficient', totalReturnPct: null, netTotalReturnPct: null }),
    );
    renderPortfolio();

    const xirr = await tile('Net XIRR');
    expect(xirr.getByText('Needs 30+ days of history')).toBeInTheDocument();
    expect(xirr.getByRole('img', { name: /first purchase at least 30 days before today/ })).toHaveTextContent('—');
    const net = await tile('Net total return');
    expect(net.getAllByRole('img', { name: /no invested capital or no price yet/ }).length).toBeGreaterThan(0);
  });

  it('says that alpha is a price return, before dividends and charges', async () => {
    summaryHas(buildIncome());
    renderPortfolio();

    expect(
      (await tile('Alpha')).getByText('XIRR minus benchmark XIRR · price return, before dividends and charges'),
    ).toBeInTheDocument();
  });

  it('is a section a keyboard and a screen reader find by name, with the Net XIRR reason reachable by Tab', async () => {
    summaryHas(buildIncome({ netXirr: null, netXirrState: 'no_data' }));
    renderPortfolio();

    const income = within(await region());
    expect(income.getByRole('img', { name: /no cash flows yet/ })).toHaveAttribute('tabindex', '0');
  });
});
