import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { brokerHistoryHandlers } from '@/test/brokerHistoryFixtures';
import { buildImportReport } from '@/test/dividendImportFixtures';
import { csvFile, stubUploads } from '@/test/multipart';
import { server } from '@/test/server';
import { chooseFile, openDividendImport } from './dividendImportTesting';
import {
  renderPortfolio,
  serveDividendImport,
  serveDividends,
  serveTrades,
  tradesPanel,
  warmDividends,
} from './testing';

stubUploads();

beforeAll(warmDividends, 60_000);

const wrongKind = (detected: string) => ({
  status: 400,
  body: { title: 'This is not the kind of file this import reads.', code: 'portfolio.wrong_file_kind', detected },
});

describe('A dividend file dropped on the trade import', () => {
  it('is recognised: the owner is told, with a button that opens the dividend import', async () => {
    const trades = serveTrades();
    serveDividends();
    serveDividendImport();
    trades.config.previewProblem = wrongKind('dividends');
    const { router } = renderPortfolio('?trades=import');
    const panel = within(await tradesPanel());
    await userEvent.upload(panel.getByLabelText('Tradebook file (CSV)'), csvFile('Date,Scrip Name\n', 'payouts.csv'));
    await userEvent.click(panel.getByRole('button', { name: 'Preview' }));

    expect(
      await panel.findByText('This is a dividend file, not a tradebook. Nothing was imported.'),
    ).toBeInTheDocument();
    await userEvent.click(panel.getByRole('button', { name: 'Import dividends' }));

    const dialog = within(await screen.findByRole('dialog', { name: 'Import dividends' }));
    expect(dialog.getByLabelText('Dividend list file (CSV)')).toBeInTheDocument();
    expect(router.state.location.search).toBe('?cash=import');
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Trades' })).not.toBeInTheDocument();
    });
  });

  it('explains a tradebook without a side column and offers no redirect', async () => {
    const trades = serveTrades();
    trades.config.previewProblem = wrongKind('tradebook_without_sides');
    renderPortfolio('?trades=import');
    const panel = within(await tradesPanel());
    await userEvent.upload(panel.getByLabelText('Tradebook file (CSV)'), csvFile('Date,Segment\n'));
    await userEvent.click(panel.getByRole('button', { name: 'Preview' }));

    expect(await panel.findByText(/it has no Buy\/Sell column/)).toBeInTheDocument();
    expect(panel.queryByRole('button', { name: 'Import dividends' })).not.toBeInTheDocument();
    expect(panel.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
  });
});

describe('A tradebook dropped on the dividend import', () => {
  it('is recognised: the owner is told, with a button back to the trade import', async () => {
    const { dialog, fake, router } = await openDividendImport();
    serveTrades();
    fake.config.previewProblem = wrongKind('trades');
    await chooseFile(dialog);
    await userEvent.click(dialog.getByRole('button', { name: 'Preview' }));

    expect(
      await dialog.findByText('This is a tradebook, not a dividend list. Nothing was imported.'),
    ).toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: 'Import trades' }));

    const panel = within(await tradesPanel());
    expect(panel.getByLabelText('Tradebook file (CSV)')).toBeInTheDocument();
    expect(router.state.location.search).toBe('?trades=import');
  });

  it('explains a tradebook without a side column, with no redirect', async () => {
    const { dialog, fake } = await openDividendImport();
    fake.config.previewProblem = wrongKind('tradebook_without_sides');
    await chooseFile(dialog);
    await userEvent.click(dialog.getByRole('button', { name: 'Preview' }));

    expect(await dialog.findByText(/it has no Buy\/Sell column/)).toBeInTheDocument();
    expect(dialog.queryByRole('button', { name: 'Import trades' })).not.toBeInTheDocument();
  });
});

describe('A statement of amounts dropped on the dividend import', () => {
  it('is explained: nothing says the amounts are dividends, and no redirect helps', async () => {
    const { dialog, fake } = await openDividendImport();
    fake.config.previewProblem = wrongKind('statement');
    await chooseFile(dialog);
    await userEvent.click(dialog.getByRole('button', { name: 'Preview' }));

    expect(await dialog.findByText(/This looks like a statement of amounts, not a dividend list/)).toHaveTextContent(
      'Use a Dividend Paid column (or add a Dividend Per Share column beside the Amount) and try again.',
    );
    expect(dialog.queryByRole('button', { name: 'Import trades' })).not.toBeInTheDocument();
    expect(dialog.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
  });

  it('is not a kind the trade import knows: it shows the problem’s own message', async () => {
    const trades = serveTrades();
    trades.config.previewProblem = wrongKind('statement');
    renderPortfolio('?trades=import');
    const panel = within(await tradesPanel());
    await userEvent.upload(panel.getByLabelText('Tradebook file (CSV)'), csvFile('Date,Amount\n'));
    await userEvent.click(panel.getByRole('button', { name: 'Preview' }));

    expect(await panel.findByText('This is not the kind of file this import reads.')).toBeInTheDocument();
    expect(panel.queryByText(/statement of amounts/)).not.toBeInTheDocument();
  });
});

describe('Dividends dated before the first lot', () => {
  const before = (count: number) =>
    buildImportReport({ beforeFirstLot: { count, amount: 3_200, firstLotDate: '2025-01-02' } });

  it('says that net XIRR needs earlier history and opens "Read from an earlier date" for the account', async () => {
    const bodies: string[] = [];
    server.use(
      ...brokerHistoryHandlers,
      http.post('/api/accounts/:id/sync/broker-history', async ({ request }) => {
        bodies.push(await request.text());
        return HttpResponse.json({ trades: null, ledger: null });
      }),
    );
    const { dialog, fake } = await openDividendImport();
    fake.config.report = before(4);
    await chooseFile(dialog);
    await userEvent.click(dialog.getByRole('button', { name: 'Preview' }));

    expect(
      await dialog.findByText(
        '4 dividends are dated before your first lot (2 Jan 2025) — net XIRR needs earlier history. They add ₹3,200.00.',
      ),
    ).toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: 'Read from an earlier date' }));
    const earlier = within(await screen.findByRole('dialog', { name: 'Read from an earlier date' }));
    expect(earlier.getByText('Test Dhan Main')).toBeInTheDocument();
    await userEvent.type(earlier.getByLabelText('Read from'), '2023-04-01');
    await userEvent.click(earlier.getByRole('button', { name: 'Read from this date' }));

    await waitFor(() => {
      expect(bodies.map((body) => JSON.parse(body) as unknown)).toEqual([{ from: '2023-04-01' }]);
    });
    expect(await screen.findByText(/Reading your trades and ledger from the earlier date/)).toBeInTheDocument();
  });

  it('says nothing when no dividend is before the first lot', async () => {
    const { dialog, fake } = await openDividendImport();
    fake.config.report = before(0);
    await chooseFile(dialog);
    await userEvent.click(dialog.getByRole('button', { name: 'Preview' }));

    await dialog.findByRole('region', { name: /Preview of/ });
    expect(dialog.queryByText(/dated before your first lot/)).not.toBeInTheDocument();
  });
});
