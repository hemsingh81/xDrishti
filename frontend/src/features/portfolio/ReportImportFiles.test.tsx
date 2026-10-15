import { fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { bigFile, csvFile, stubUploads } from '@/test/multipart';
import { ids } from '@/test/fixtures';
import { buildDividendRoutePreview, buildGtrPreview } from '@/test/reportImportFixtures';
import { chooseFiles, dpFile, gtrFile, openImportFiles, previewFiles, sampleCsv } from './reportImportTesting';
import { renderPortfolio, serveReportImport, serveTrades, warmReportImport } from './testing';

stubUploads();

beforeAll(warmReportImport, 60_000);

describe('Import files: the entry points', () => {
  it('opens from the link ?import=open and names the account the files go to', async () => {
    const { dialog } = await openImportFiles();

    expect(dialog.getByText(/Files go to Test Dhan Main/)).toBeInTheDocument();
    expect(dialog.getByLabelText('Report files (CSV, XLS or XLSX)')).toHaveAttribute('type', 'file');
    expect(dialog.getByLabelText('Report files (CSV, XLS or XLSX)')).toHaveAttribute('multiple');
    expect(dialog.getByRole('button', { name: 'Preview' })).toBeDisabled();
  });

  it('is offered by the sync bar', async () => {
    serveTrades();
    serveReportImport();
    renderPortfolio();

    await userEvent.click(await screen.findByRole('button', { name: 'Import files' }));

    expect(await screen.findByRole('dialog', { name: 'Import files' })).toBeInTheDocument();
  });

  it('is offered by the Trades panel and, closed, returns to it', async () => {
    serveTrades();
    serveReportImport();
    renderPortfolio('?trades=import');
    const trades = within(await screen.findByRole('dialog', { name: 'Trades' }));

    await userEvent.click(trades.getByRole('button', { name: 'Import files' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Import files' }));
    await userEvent.click(dialog.getByRole('button', { name: 'Close' }));

    expect(screen.queryByRole('dialog', { name: 'Import files' })).not.toBeInTheDocument();
    expect(await screen.findByRole('dialog', { name: 'Trades' })).toBeInTheDocument();
  });

  it('is offered by the Cash & dividends panel', async () => {
    serveTrades();
    serveReportImport();
    renderPortfolio('?cash=open');
    const cash = within(await screen.findByRole('dialog', { name: 'Cash & dividends' }));

    await userEvent.click(await cash.findByRole('button', { name: 'Import files' }));

    expect(await screen.findByRole('dialog', { name: 'Import files' })).toBeInTheDocument();
  });
});

describe('Import files: choosing files', () => {
  it('lists the chosen files with their size and takes one away again', async () => {
    const { dialog } = await openImportFiles();

    await chooseFiles(dialog, [csvFile('x'.repeat(2048), 'payouts.csv'), dpFile()]);

    const list = within(dialog.getByRole('list', { name: 'Chosen files' }));
    expect(list.getAllByRole('listitem')).toHaveLength(2);
    expect(list.getByText('2 KB')).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Preview 2 files' })).toBeEnabled();
    await userEvent.click(dialog.getByRole('button', { name: 'Remove payouts.csv' }));
    expect(dialog.queryByText('payouts.csv')).not.toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Preview' })).toBeEnabled();
  });

  it('takes files dropped on the area, and reaches the input with the keyboard', async () => {
    const { dialog } = await openImportFiles();
    const input = dialog.getByLabelText('Report files (CSV, XLS or XLSX)');
    const area = input.closest('label');
    if (!area) throw new Error('no drop area');

    const dropped = [gtrFile(), dpFile()];
    fireEvent.dragOver(area, { dataTransfer: { files: dropped } });
    expect(dialog.getByText('Drop the files to choose them')).toBeInTheDocument();
    fireEvent.drop(area, { dataTransfer: { files: dropped } });

    expect(dialog.getByText('global-transaction-fy26.csv')).toBeInTheDocument();
    expect(dialog.getByText('dp-charges-fy26.xls')).toBeInTheDocument();
    input.focus();
    expect(input).toHaveFocus();
  });

  it('keeps a file once when it is chosen twice', async () => {
    const { dialog } = await openImportFiles();

    await chooseFiles(dialog, [gtrFile()]);
    await chooseFiles(dialog, [gtrFile()]);

    expect(within(dialog.getByRole('list', { name: 'Chosen files' })).getAllByRole('listitem')).toHaveLength(1);
  });

  it.each([
    ['a PDF', csvFile('x', 'statement.pdf'), /“statement\.pdf” is not a CSV, XLS or XLSX file/],
    ['an empty file', csvFile('', 'empty.csv'), /“empty\.csv” is empty/],
    [
      'a file over 10 MB',
      bigFile(12 * 1024 * 1024 + 1, 'huge.csv'),
      /“huge\.csv” is 12 MB; the limit is 10 MB per file/,
    ],
  ])('refuses %s before uploading anything', async (_name, file, message) => {
    const { dialog, fake } = await openImportFiles();

    await chooseFiles(dialog, [file]);

    expect(await dialog.findByText(message)).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Preview' })).toBeDisabled();
    expect(fake.calls.previews).toHaveLength(0);
  });

  it('refuses a 21st file', async () => {
    const { dialog } = await openImportFiles();

    await chooseFiles(
      dialog,
      Array.from({ length: 21 }, (_, n) => csvFile(`${sampleCsv}${String(n)}`, `report-${String(n)}.csv`)),
    );

    expect(within(dialog.getByRole('list', { name: 'Chosen files' })).getAllByRole('listitem')).toHaveLength(20);
    expect(dialog.getByText(/“report-20\.csv” was not added: at most 20 files/)).toBeInTheDocument();
  });

  it('refuses a file that would take the files over 40 MB together', async () => {
    const { dialog } = await openImportFiles();
    const tenMb = 10 * 1024 * 1024;

    await chooseFiles(
      dialog,
      [1, 2, 3, 4, 5].map((n) => bigFile(tenMb, `part-${String(n)}.csv`)),
    );

    expect(within(dialog.getByRole('list', { name: 'Chosen files' })).getAllByRole('listitem')).toHaveLength(4);
    expect(
      dialog.getByText(/“part-5\.csv” was not added: the files together may be at most 40 MB/),
    ).toBeInTheDocument();
  });

  it('sends the account and every file as one form', async () => {
    const { dialog, fake } = await openImportFiles({ previews: [buildGtrPreview()] });

    const region = await previewFiles(dialog, [gtrFile()]);

    expect(fake.calls.previews).toHaveLength(1);
    expect(fake.calls.previews[0]?.fields.accountId).toBe(ids.main);
    expect(fake.calls.previews[0]?.files).toEqual(['global-transaction-fy26.csv']);
    expect(fake.calls.previews[0]?.choices).toEqual([]);
    expect(within(region).getByRole('heading', { name: /nothing is stored yet/ })).toHaveFocus();
  });

  it('asks which account first when the page is not on one account', async () => {
    const { twoPortfolioAccounts } = await import('./testing');
    const { server } = await import('@/test/server');
    server.use(twoPortfolioAccounts());
    const { dialog } = await openImportFiles();

    expect(dialog.getByText('Choose the account the files belong to first.')).toBeInTheDocument();
    expect(dialog.getByLabelText('Report files (CSV, XLS or XLSX)')).toBeDisabled();
    await userEvent.selectOptions(dialog.getByRole('combobox', { name: 'Account' }), 'Test Dhan Main');
    expect(dialog.getByLabelText('Report files (CSV, XLS or XLSX)')).toBeEnabled();
  });

  it('shows what the server said when the files cannot be read, with Retry', async () => {
    const { dialog, fake } = await openImportFiles({ previews: [buildGtrPreview()] });
    fake.config.previewProblem = {
      status: 400,
      body: { title: 'Invalid', code: 'portfolio.invalid', errors: { files: ['At most 20 files at a time.'] } },
    };
    await chooseFiles(dialog, [gtrFile()]);

    await userEvent.click(dialog.getByRole('button', { name: 'Preview' }));
    expect(await dialog.findByText('At most 20 files at a time.')).toBeInTheDocument();

    fake.config.previewProblem = null;
    await userEvent.click(dialog.getByRole('button', { name: 'Retry' }));
    expect(await dialog.findByRole('region', { name: 'global-transaction-fy26.csv' })).toBeInTheDocument();
  });

  it('puts a dividend list on its own import', async () => {
    const { dialog } = await openImportFiles({ previews: [buildDividendRoutePreview()] });

    await previewFiles(dialog, [csvFile(sampleCsv, 'dividends-fy26.csv')]);
    await userEvent.click(dialog.getByRole('button', { name: 'Open the dividend import' }));

    expect(await screen.findByRole('dialog', { name: 'Import dividends' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Import files' })).not.toBeInTheDocument();
  });
});
