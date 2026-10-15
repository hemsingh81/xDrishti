import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildLedgerEntry, ledgerIds } from '@/test/brokerHistoryFixtures';
import { holdingIds, ids } from '@/test/fixtures';
import { buildTrade } from '@/test/tradeFixtures';
import { renderPortfolio, serveDividends, serveTrades, warmDividends, warmTrades } from './testing';

// The places the bell and the pop-ups lead to (ADR 0027): a holding on its Trades section (with the rebuild preview), the
// Cash & dividends panel on the suggestions, and the holdings that are not in the instrument master list.
beforeAll(async () => {
  await warmTrades();
  await warmDividends();
}, 60_000);

describe('Link: a holding on its Trades section', () => {
  it('opens the rebuild preview of the holding and takes the request out of the address', async () => {
    const fake = serveTrades({ trades: [buildTrade()] });
    const { router } = renderPortfolio(`?holding=${holdingIds.infy}&section=trades&rebuild=preview`);

    const dialog = within(await screen.findByRole('dialog', { name: 'Rebuild the lots of INFY' }));
    expect(await dialog.findByText('Nothing has changed yet: this is what the rebuild would do.')).toBeInTheDocument();
    expect(fake.calls.rebuilds).toEqual([{ holdingId: holdingIds.infy, dryRun: true }]);
    await waitFor(() => {
      expect(router.state.location.search).toBe(`?holding=${holdingIds.infy}`);
    });
  });

  it('scrolls to the Trades section and puts the keyboard on its title, without opening the preview', async () => {
    serveTrades({ trades: [buildTrade()] });
    const { router } = renderPortfolio(`?holding=${holdingIds.infy}&section=trades`);

    const drawer = within(await screen.findByLabelText('Holding details'));
    const title = await drawer.findByRole('heading', { name: 'Trades' });
    await waitFor(() => {
      expect(title).toHaveFocus();
    });
    expect(screen.queryByRole('dialog', { name: 'Rebuild the lots of INFY' })).not.toBeInTheDocument();
    await waitFor(() => {
      expect(router.state.location.search).toBe(`?holding=${holdingIds.infy}`);
    });
  });

  it('opens the holding as before when the link asks for no section', async () => {
    serveTrades({ trades: [buildTrade()] });
    renderPortfolio(`?holding=${holdingIds.infy}`);

    const drawer = within(await screen.findByLabelText('Holding details'));
    expect(await drawer.findByRole('heading', { name: 'Trades' })).not.toHaveFocus();
    expect(screen.queryByRole('dialog', { name: 'Rebuild the lots of INFY' })).not.toBeInTheDocument();
  });
});

describe('Link: Cash & dividends on the suggestions of an account', () => {
  it('opens the panel for the account of the link with the keyboard on the suggested dividends', async () => {
    serveDividends({
      suggestions: [buildLedgerEntry({ id: ledgerIds(11), date: '2026-09-12', amount: 450, description: 'Dividend' })],
    });
    renderPortfolio(`?cash=suggestions&account=${ids.main}`);

    const panel = within(await screen.findByRole('dialog', { name: 'Cash & dividends' }));
    const title = await panel.findByRole('heading', { name: 'Suggested dividends' });
    await waitFor(() => {
      expect(title).toHaveFocus();
    });
    expect(panel.getByRole('table', { name: 'Suggested dividends' })).toBeInTheDocument();
  });
});

describe('Link: the holdings that are not in the instrument master list', () => {
  it('lists only those, says why, and gives the way back to all holdings', async () => {
    const { router } = renderPortfolio(`?unmatched=1&account=${ids.main}`);

    expect(await screen.findByText(/1 holding is not in the instrument master list/)).toBeInTheDocument();
    const grid = screen.getByRole('grid', { name: 'Holdings' });
    expect(await within(grid).findByText('SMALLCO')).toBeInTheDocument();
    expect(within(grid).queryByText('RELIANCE')).not.toBeInTheDocument();
    expect(within(grid).queryByText('INFY')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Show all holdings' }));

    expect(await within(grid).findByText('RELIANCE')).toBeInTheDocument();
    expect(within(grid).getByText('SMALLCO')).toBeInTheDocument();
    expect(screen.queryByText(/not in the instrument master list/)).not.toBeInTheDocument();
    expect(router.state.location.search).toBe(`?account=${ids.main}`);
  });

  it('sets a grouping aside while the unmatched holdings are shown', async () => {
    renderPortfolio('?unmatched=1&groupBy=sector');

    const grid = await screen.findByRole('grid', { name: 'Holdings' });
    expect(await within(grid).findByText('SMALLCO')).toBeInTheDocument();
    expect(within(grid).queryByText('RELIANCE')).not.toBeInTheDocument();
  });
});
