import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { QueryClient } from '@tanstack/react-query';
import { answerLedger, buildLedgerEntry, ledgerIds, sampleLedger } from '@/test/brokerHistoryFixtures';
import { stubUploads } from '@/test/multipart';
import {
  buildCostBatch,
  buildCostImportResult,
  buildDpPreview,
  buildFileResult,
  costBatchIds,
} from '@/test/reportImportFixtures';
import { server } from '@/test/server';
import { findToast } from '@/test/toast';
import { batchIds, buildBatch, buildImportResult } from '@/test/tradeFixtures';
import { invalidateReportImports } from './api/reportMutations';
import { dpFile, openImportFiles, previewFiles } from './reportImportTesting';
import { renderPortfolio, serveReportImport, serveTrades, tradesPanel, warmReportImport, warmTrades } from './testing';

stubUploads();

beforeAll(async () => {
  await warmTrades();
  await warmReportImport();
}, 60_000);

const costOlder = () =>
  buildCostBatch({ id: costBatchIds.second, fileName: 'dp-charges-fy25.xls', createdAt: '2026-10-10T10:30:00Z' });

describe('Import history: tradebooks and depository-charges sheets', () => {
  it('lists both kinds, newest first, each with its kind', async () => {
    serveTrades({ batches: [buildBatch({ createdAt: '2026-10-11T09:30:00Z' })] });
    serveReportImport({ costBatches: [buildCostBatch(), costOlder()] });
    renderPortfolio('?trades=history');
    const panel = within(await tradesPanel());

    const list = within(await panel.findByRole('list', { name: 'Imports' }));
    const items = list.getAllByRole('listitem');
    expect(items).toHaveLength(3);
    expect(within(items[0]!).getByText('dp-charges-fy26.xls')).toBeInTheDocument();
    expect(within(items[0]!).getByText('DP charges')).toBeInTheDocument();
    expect(within(items[0]!).getByText(/1 charge stored \(₹12\.50\)/)).toBeInTheDocument();
    expect(within(items[1]!).getByText('tradebook-fy26.csv')).toBeInTheDocument();
    expect(within(items[1]!).getByText('Trades')).toBeInTheDocument();
    expect(within(items[2]!).getByText('dp-charges-fy25.xls')).toBeInTheDocument();
    expect(panel.getByText('Showing 3 of 3')).toBeInTheDocument();
  });

  it('undoes a depository-charges import from the list', async () => {
    serveTrades();
    const fake = serveReportImport({ costBatches: [buildCostBatch()] });
    renderPortfolio('?trades=history');
    const panel = within(await tradesPanel());

    await userEvent.click(await panel.findByRole('button', { name: 'Undo the import of dp-charges-fy26.xls' }));
    const undo = within(await screen.findByRole('dialog', { name: 'Undo this import?' }));
    await userEvent.click(undo.getByRole('button', { name: 'Undo import' }));

    expect((await findToast()).textContent).toMatch(/Import undone: 1 charge voided\./);
    expect(fake.calls.costUndone).toEqual([costBatchIds.first]);
    expect(await panel.findByText(/Undone .*none of its charges count now/)).toBeInTheDocument();
    await waitFor(() => {
      expect(panel.getByRole('list', { name: 'Imports' })).toHaveFocus();
    });
  });

  it('is also a tab of the Import files dialog', async () => {
    const { dialog } = await openImportFiles({ costBatches: [buildCostBatch()] });

    await userEvent.click(dialog.getByRole('tab', { name: 'History' }));

    const list = within(await dialog.findByRole('list', { name: 'Imports' }));
    expect(list.getByText('dp-charges-fy26.xls')).toBeInTheDocument();
  });

  it('says there is nothing yet', async () => {
    serveTrades();
    serveReportImport();
    renderPortfolio('?trades=history');
    const panel = within(await tradesPanel());

    expect(
      await panel.findByText(/No tradebook has been imported yet\. Use Import CSV to load one/),
    ).toBeInTheDocument();
    expect(
      panel.getByText(/Depository-charges sheets imported with Import files are listed here too/),
    ).toBeInTheDocument();
  });

  it('shows an error with Retry when the depository-charges imports cannot be loaded', async () => {
    serveTrades({ batches: [buildBatch()] });
    serveReportImport();
    let fail = true;
    server.use(
      http.get('/api/portfolio/cost-batches', () =>
        fail
          ? new HttpResponse(null, { status: 500 })
          : HttpResponse.json({ items: [buildCostBatch()], page: 1, pageSize: 50, total: 1 }),
      ),
    );
    renderPortfolio('?trades=history');
    const panel = within(await tradesPanel());

    expect(await panel.findByText('The imports could not be loaded.')).toBeInTheDocument();
    fail = false;
    await userEvent.click(panel.getByRole('button', { name: 'Retry' }));
    expect(await panel.findByText('dp-charges-fy26.xls')).toBeInTheDocument();
    expect(panel.getByText('tradebook-fy26.csv')).toBeInTheDocument();
  });

  it('shows the imports of the dialog again after an import (the new batch is listed)', async () => {
    const { dialog } = await openImportFiles({ previews: [buildDpPreview()] });
    await previewFiles(dialog, [dpFile()]);
    await userEvent.click(dialog.getByRole('button', { name: 'Import 1 file' }));
    await dialog.findByRole('heading', { name: 'Import result' });

    await userEvent.click(dialog.getByRole('button', { name: 'See it in History' }));

    const list = within(await dialog.findByRole('list', { name: 'Imports' }));
    expect(list.getByText('dp-charges-fy26.xls')).toBeInTheDocument();
  });
});

describe('The ledger marks what was imported', () => {
  it('marks an entry that came from a report, and not one read from Dhan', async () => {
    serveTrades();
    serveReportImport();
    server.use(
      http.get('/api/accounts/:id/ledger', ({ request }) =>
        HttpResponse.json(
          answerLedger(
            [
              buildLedgerEntry({
                id: ledgerIds(21),
                date: '2026-10-10',
                voucherNo: 'DP-1',
                description: 'DP charges',
                narration: '',
                class: 'charges',
                amount: -12.5,
                source: 'import',
              }),
              ...sampleLedger,
            ],
            new URL(request.url).searchParams,
          ),
        ),
      ),
    );
    renderPortfolio('?cash=open');
    const panel = within(await screen.findByRole('dialog', { name: 'Cash & dividends' }));

    const table = within(await panel.findByRole('table', { name: /ledger/i }));
    const imported = within(table.getByRole('row', { name: /Voucher DP-1/ }));
    expect(imported.getByText('Imported')).toBeInTheDocument();
    expect(within(table.getByRole('row', { name: /Bank payin/ })).queryByText('Imported')).not.toBeInTheDocument();
  });

  it('is read again after a depository-charges import, and only then', async () => {
    serveTrades();
    serveReportImport({ previews: [buildDpPreview()] });
    let reads = 0;
    server.use(
      http.get('/api/accounts/:id/ledger', ({ request }) => {
        reads += 1;
        return HttpResponse.json(answerLedger(sampleLedger, new URL(request.url).searchParams));
      }),
    );
    renderPortfolio('?cash=open&import=open');
    const dialog = within(await screen.findByRole('dialog', { name: 'Import files' }));
    await waitFor(() => {
      expect(reads).toBeGreaterThan(0);
    });
    const before = reads;
    await previewFiles(dialog, [dpFile()]);
    await userEvent.click(dialog.getByRole('button', { name: 'Import 1 file' }));
    await dialog.findByRole('heading', { name: 'Import result' });

    await waitFor(() => {
      expect(reads).toBeGreaterThan(before);
    });
  });
});

describe('What an import reloads', () => {
  const keysOf = (results: Parameters<typeof invalidateReportImports>[1]) => {
    const client = new QueryClient();
    const spy = vi.spyOn(client, 'invalidateQueries').mockResolvedValue();
    void invalidateReportImports(client, results);
    return spy.mock.calls.map(([filters]) => JSON.stringify(filters?.queryKey));
  };
  const stored = buildFileResult({
    kind: 'global_transaction_report',
    trades: buildImportResult({ batch: buildBatch({ id: batchIds.second }), rebuilt: false }),
  });

  it('reloads the trades and the income, not the holdings, for trades that left the lots alone', () => {
    const keys = keysOf([stored]);

    expect(keys).toContain(JSON.stringify(['portfolio', 'trades']));
    expect(keys).toContain(JSON.stringify(['portfolio', 'holdings']));
    expect(keys).not.toContain(JSON.stringify(['portfolio', 'lots']));
    expect(keys).not.toContain(JSON.stringify(['portfolio', 'broker-history', 'ledger']));
  });

  it('reloads the holdings, lots and figures too when the lots were rebuilt', () => {
    const keys = keysOf([buildFileResult({ trades: buildImportResult({ rebuilt: true }) })]);

    expect(keys).toContain(JSON.stringify(['portfolio', 'lots']));
    expect(keys).toContain(JSON.stringify(['portfolio', 'summary']));
  });

  it('reloads the income, the ledger and the cost imports for depository charges', () => {
    const keys = keysOf([buildFileResult({ kind: 'dp_charges', costs: buildCostImportResult() })]);

    expect(keys).toContain(JSON.stringify(['portfolio', 'broker-history', 'ledger']));
    expect(keys).toContain(JSON.stringify(['portfolio', 'trades', 'cost-batches']));
    expect(keys).toContain(JSON.stringify(['portfolio', 'summary']));
    expect(keys).not.toContain(JSON.stringify(['portfolio', 'lots']));
  });

  it('reloads nothing for a demat statement or for files in which nothing was stored', () => {
    expect(
      keysOf([
        buildFileResult({ kind: 'demat_holding', demat: { namesLearned: 3, namesKnown: 1 } }),
        buildFileResult({ kind: 'dp_charges', status: 'nothing_new', costs: buildCostImportResult({ batch: null }) }),
        buildFileResult({ kind: 'trades', trades: buildImportResult({ batch: null }) }),
      ]),
    ).toEqual([]);
  });
});
