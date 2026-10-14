import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildDividend, dividendIds } from '@/test/dividendFixtures';
import { buildDividendBatch, dividendBatchIds } from '@/test/dividendImportFixtures';
import { stubUploads } from '@/test/multipart';
import { holdingIds } from '@/test/fixtures';
import { openDividendImport, previewFile } from './dividendImportTesting';
import { renderPortfolio, serveDividends, warmDividends } from './testing';

stubUploads();

beforeAll(warmDividends, 60_000);

const undone = buildDividendBatch({
  id: dividendBatchIds.second,
  fileName: 'older-payouts.csv',
  status: 'undone',
  activeDividends: 0,
  undoneAt: '2026-10-13T08:00:00Z',
  createdAt: '2026-09-01T09:30:00Z',
});

async function history(batches = [buildDividendBatch(), undone]) {
  const view = await openDividendImport({ batches });
  await userEvent.click(view.dialog.getByRole('tab', { name: 'History' }));
  const list = await view.dialog.findByRole('list', { name: 'Dividend imports' });
  return { ...view, list: within(list) };
}

describe('Import dividends: history', () => {
  it('lists the imported files with their numbers and Undo while they count', async () => {
    const { list } = await history();

    const first = within(list.getAllByRole('listitem')[0]!);
    expect(first.getByText('dividends-sample.csv')).toBeInTheDocument();
    expect(first.getByText('Imported')).toBeInTheDocument();
    expect(first.getByText('7 valid rows · 5 dividends stored (₹1,750.00) · 1 duplicate')).toBeInTheDocument();
    expect(first.getByText('5 dividends still count.')).toBeInTheDocument();
    expect(first.getByRole('button', { name: 'Undo the import of dividends-sample.csv' })).toBeInTheDocument();
    const second = within(list.getAllByRole('listitem')[1]!);
    expect(second.getByText('Undone')).toBeInTheDocument();
    expect(second.getByText(/none of its dividends count now/)).toBeInTheDocument();
    expect(second.queryByRole('button', { name: /^Undo/ })).not.toBeInTheDocument();
  });

  it('undoes an import after asking, tells it in a toast and moves the keyboard to the list', async () => {
    const { dialog, list, fake } = await history();

    await userEvent.click(list.getByRole('button', { name: 'Undo the import of dividends-sample.csv' }));
    const ask = within(await screen.findByRole('dialog', { name: 'Undo this import?' }));
    await userEvent.click(ask.getByRole('button', { name: 'Undo import' }));

    expect(await screen.findByText('Import undone: 5 dividends voided.')).toBeInTheDocument();
    await waitFor(() => {
      expect(dialog.getAllByText('Undone')).toHaveLength(2);
    });
    expect(fake.calls.undone).toEqual([dividendBatchIds.first]);
    await waitFor(() => {
      expect(dialog.getByRole('list', { name: 'Dividend imports' })).toHaveFocus();
    });
  });

  it('leaves the import alone when the question is cancelled', async () => {
    const { list, fake } = await history();

    await userEvent.click(list.getByRole('button', { name: 'Undo the import of dividends-sample.csv' }));
    await userEvent.click(
      within(await screen.findByRole('dialog', { name: 'Undo this import?' })).getByRole('button', { name: 'Cancel' }),
    );

    expect(fake.calls.undone).toEqual([]);
    expect(list.getByRole('button', { name: 'Undo the import of dividends-sample.csv' })).toBeInTheDocument();
  });

  it('says so when nothing was imported yet, and when the list cannot be loaded (Retry)', async () => {
    const { dialog, fake } = await openDividendImport();
    fake.config.batchesProblem = { status: 500, body: { title: 'The imports are not available.' } };
    await userEvent.click(dialog.getByRole('tab', { name: 'History' }));

    expect(await dialog.findByText('The imports are not available.')).toBeInTheDocument();
    fake.config.batchesProblem = null;
    await userEvent.click(dialog.getByRole('button', { name: 'Retry' }));
    expect(await dialog.findByText(/No dividend file has been imported for this account yet/)).toBeInTheDocument();
  });

  it('pages through many imports with Show more', async () => {
    const many = Array.from({ length: 60 }, (_, i) =>
      buildDividendBatch({
        id: `00000000-0000-4000-8000-00000000169${String(i).padStart(2, '0')}`,
        fileName: `file-${String(i)}.csv`,
      }),
    );
    const { dialog, list } = await history(many);

    expect(list.getAllByRole('listitem')).toHaveLength(50);
    expect(dialog.getByText('Showing 50 of 60')).toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: 'Show more' }));
    await waitFor(() => {
      expect(list.getAllByRole('listitem')).toHaveLength(60);
    });
  });

  it('shows the new import after "See it in History", and keeps a half-done preview while History is open', async () => {
    const { dialog } = await openDividendImport();
    await previewFile(dialog);
    await userEvent.click(dialog.getByRole('tab', { name: 'History' }));
    await userEvent.click(dialog.getByRole('tab', { name: 'Import file' }));
    expect(dialog.getByRole('region', { name: /Preview of/ })).toBeInTheDocument();

    await userEvent.click(dialog.getByRole('button', { name: /^Import \d+ dividends$/ }));
    await userEvent.click(await dialog.findByRole('button', { name: 'See it in History' }));

    const list = within(await dialog.findByRole('list', { name: 'Dividend imports' }));
    expect(await list.findByText('dividends-sample.csv')).toBeInTheDocument();
  });
});

describe('Dividends list: imported dividends', () => {
  it('marks a dividend from a file with a CSV chip, shows its shares and the file’s name for an unallocated one', async () => {
    const csv = buildDividend({
      id: dividendIds.first,
      source: 'csv',
      quantity: 120,
      holdingId: holdingIds.reliance,
      batchId: dividendBatchIds.first,
      note: null,
    });
    const orphan = buildDividend({
      id: dividendIds.second,
      source: 'csv',
      holdingId: null,
      symbol: null,
      isin: null,
      scripName: 'Initech Industries Ltd',
      quantity: 40,
      payDate: '2025-03-01',
      amount: 90,
    });
    const manual = buildDividend({ id: dividendIds.third, payDate: '2025-01-01', quantity: null });
    const { cash } = await openDividendImportPanel([csv, orphan, manual]);

    const table = within(await cash.findByRole('table', { name: 'Dividends of Test Dhan Main' }));
    const csvRow = within(table.getByRole('row', { name: /5 Dec 2025/ }));
    expect(csvRow.getByText('CSV')).toBeInTheDocument();
    expect(csvRow.getByText('120')).toBeInTheDocument();
    expect(csvRow.getByText(/^Imported by owner/)).toBeInTheDocument();
    const orphanRow = within(table.getByRole('row', { name: /1 Mar 2025/ }));
    expect(orphanRow.getByText('Unallocated')).toBeInTheDocument();
    expect(orphanRow.getByText('Initech Industries Ltd')).toBeInTheDocument();
    expect(orphanRow.getByText('40')).toBeInTheDocument();
    const manualRow = within(table.getByRole('row', { name: /1 Jan 2025/ }));
    expect(manualRow.queryByText('CSV')).not.toBeInTheDocument();
    expect(manualRow.getByText(/^Entered by owner/)).toBeInTheDocument();
  });
});

async function openDividendImportPanel(dividends: ReturnType<typeof buildDividend>[]) {
  serveDividends({ dividends });
  renderPortfolio('?cash=open');
  return { cash: within(await screen.findByRole('dialog', { name: 'Cash & dividends' })) };
}
