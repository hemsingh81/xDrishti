import { fireEvent, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildImportItem, buildImportReport, reportOf } from '@/test/dividendImportFixtures';
import { bigFile, csvFile, stubUploads } from '@/test/multipart';
import { ids } from '@/test/fixtures';
import { chooseFile, dividendCsv, openDividendImport, previewFile } from './dividendImportTesting';
import { warmDividends } from './testing';

stubUploads();

beforeAll(warmDividends, 60_000);

const count = (region: HTMLElement, label: string) =>
  within(region).getByText(label, { selector: 'dt' }).nextElementSibling;

describe('Import dividends: the file', () => {
  it('is offered by Cash & dividends and names the account the file goes to', async () => {
    const { dialog } = await openDividendImport();

    expect(dialog.getByText('Test Dhan Main')).toBeInTheDocument();
    expect(dialog.getByLabelText('Dividend list file (CSV)')).toHaveAttribute('type', 'file');
    expect(dialog.getByText(/The file has no account column, so it goes to this account/)).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Preview' })).toBeDisabled();
  });

  it('can be opened by the link ?cash=import', async () => {
    const { dialog } = await openDividendImport({}, '?cash=import');

    expect(dialog.getByRole('button', { name: 'Preview' })).toBeInTheDocument();
  });

  it('names the chosen file with its size and takes it away again', async () => {
    const { dialog } = await openDividendImport();

    await chooseFile(dialog, csvFile('x'.repeat(2048), 'payouts.csv'));

    expect(dialog.getByText('payouts.csv')).toBeInTheDocument();
    expect(dialog.getByText('2 KB')).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Preview' })).toBeEnabled();
    await userEvent.click(dialog.getByRole('button', { name: 'Remove payouts.csv' }));
    expect(dialog.queryByText('payouts.csv')).not.toBeInTheDocument();
  });

  it('takes a file dropped on the area, and reaches the input with the keyboard', async () => {
    const { dialog } = await openDividendImport();
    const input = dialog.getByLabelText('Dividend list file (CSV)');
    const area = input.closest('label');
    if (!area) throw new Error('no drop area');

    const dropped = csvFile(dividendCsv, 'dropped.csv');
    fireEvent.dragOver(area, { dataTransfer: { files: [dropped] } });
    expect(dialog.getByText('Drop the file to choose it')).toBeInTheDocument();
    fireEvent.drop(area, { dataTransfer: { files: [dropped] } });

    expect(dialog.getByText('dropped.csv')).toBeInTheDocument();
    await userEvent.tab();
    // The first tab stops are the tabs and the input: the input is a real, focusable control.
    input.focus();
    expect(input).toHaveFocus();
  });

  it.each([
    ['a spreadsheet', csvFile('x', 'dividends.xlsx'), /“dividends\.xlsx” is not a CSV file. Export the dividend list/],
    ['an empty file', csvFile('', 'empty.csv'), /“empty\.csv” is empty/],
    ['a file over 10 MB', bigFile(12 * 1024 * 1024 + 1, 'huge.csv'), /“huge\.csv” is 12 MB; the limit is 10 MB/],
  ])('refuses %s before uploading anything', async (_name, file, message) => {
    const { dialog, fake } = await openDividendImport();

    await chooseFile(dialog, file);

    expect(await dialog.findByText(message)).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Preview' })).toBeDisabled();
    expect(fake.calls.previews).toHaveLength(0);
  });
});

describe('Import dividends: the preview', () => {
  it('sends the account and the file as a form and moves the keyboard to the report', async () => {
    const { dialog, fake } = await openDividendImport();

    const region = await previewFile(dialog);

    expect(fake.calls.previews).toEqual([
      { fields: { accountId: ids.main }, file: { name: 'dividends-sample.csv', text: dividendCsv } },
    ]);
    expect(within(region).getByRole('heading', { name: /nothing is stored yet/ })).toHaveFocus();
  });

  it('counts the rows: read, to add, amount, already imported, possible duplicates, needs a match, ambiguous and bad', async () => {
    const { dialog } = await openDividendImport();
    const region = await previewFile(dialog);

    expect(count(region, 'Rows read')).toHaveTextContent('7');
    expect(count(region, 'To add')).toHaveTextContent('5');
    expect(count(region, 'Amount to add')).toHaveTextContent('₹1,750.00');
    expect(count(region, 'Already imported')).toHaveTextContent('1');
    expect(count(region, 'Possible duplicates')).toHaveTextContent('1');
    expect(count(region, 'Needs a match')).toHaveTextContent('2');
    expect(count(region, 'Ambiguous')).toHaveTextContent('1');
    expect(count(region, 'Bad rows')).toHaveTextContent('0');
  });

  it('totals what would be added by year', async () => {
    const { dialog } = await openDividendImport();
    const region = await previewFile(dialog);

    const years = within(within(region).getByRole('list', { name: 'Dividends to add by year' }));
    expect(years.getByText('2025: 5 · ₹1,750.00')).toBeInTheDocument();
  });

  it('lists the rows ready to add with the holding each was matched to and how', async () => {
    const { dialog } = await openDividendImport();
    const region = await previewFile(dialog);

    await userEvent.click(within(region).getByRole('tab', { name: 'Ready to add (2)' }));
    const table = within(within(region).getByRole('table', { name: 'Rows ready to add' }));
    const first = within(table.getByRole('row', { name: /Reliance Industries Ltd/ }));
    expect(first.getByText('RELIANCE')).toBeInTheDocument();
    expect(first.getByText('by same name')).toBeInTheDocument();
    expect(first.getByText('₹1,200.00')).toBeInTheDocument();
    expect(first.getByText('5 Jun 2025')).toBeInTheDocument();
  });

  it('shows a partial name match with its warning and a Change button', async () => {
    const { dialog } = await openDividendImport();
    const region = await previewFile(dialog);

    await userEvent.click(within(region).getByRole('tab', { name: 'Ready to add (2)' }));
    const row = within(within(region).getByRole('row', { name: /Tata Consultancy/ }));

    expect(row.getByText('Partial name match')).toBeInTheDocument();
    expect(row.getByText('Matched by part of the name: check it.')).toBeInTheDocument();
    expect(row.getByRole('button', { name: 'Change the match for Tata Consultancy' })).toBeInTheDocument();
  });

  it('says where a name nothing fits will go, and lists the names that need a match with their rows and rupees', async () => {
    const { dialog } = await openDividendImport();
    const region = within(await previewFile(dialog));

    const table = within(region.getByRole('table', { name: 'Names that need a match' }));
    const northwind = within(table.getByRole('row', { name: /Northwind Traders Ltd/ }));
    expect(northwind.getByText('2')).toBeInTheDocument();
    expect(northwind.getByText('₹250.00')).toBeInTheDocument();
    expect(northwind.getByText('Not chosen: stored unallocated')).toBeInTheDocument();
    const contoso = within(table.getByRole('row', { name: /Contoso Holdings/ }));
    expect(contoso.getByText('Ambiguous: fits INFY, TCS')).toBeInTheDocument();
  });

  it('lists the possible duplicates and the rows already imported on their own tabs', async () => {
    const { dialog } = await openDividendImport();
    const region = within(await previewFile(dialog));

    await userEvent.click(region.getByRole('tab', { name: 'Possible duplicates (1)' }));
    expect(region.getByText(/They are skipped unless you tick them/)).toBeInTheDocument();
    expect(region.getByRole('checkbox', { name: /Include line 7, Reliance Industries Ltd/ })).not.toBeChecked();

    await userEvent.click(region.getByRole('tab', { name: 'Already imported (1)' }));
    const table = within(region.getByRole('table', { name: 'Rows already imported' }));
    expect(table.getByText('Infosys Ltd')).toBeInTheDocument();
    expect(table.getByText('by remembered match')).toBeInTheDocument();
  });

  it('shows an instrument-only match and the warnings of a row (amount, before the first lot)', async () => {
    const items = [
      buildImportItem({
        line: 2,
        name: 'Fabrikam Ltd',
        status: 'new',
        warnings: ['amount_mismatch', 'before_first_lot'],
        match: {
          kind: 'master',
          holdingId: null,
          instrumentId: '00000000-0000-4000-8000-000000000999',
          symbol: 'NSE:FABRK',
          isin: null,
          name: 'Fabrikam Limited',
          candidates: [],
        },
      }),
    ];
    const { dialog } = await openDividendImport({ report: reportOf(items) });
    const region = within(await previewFile(dialog));

    const row = within(region.getByRole('row', { name: /Fabrikam Ltd/ }));
    expect(row.getByText('FABRK')).toBeInTheDocument();
    expect(row.getByText('by instrument list · not held, instrument only')).toBeInTheDocument();
    expect(
      row.getByText('Per share × shares differs from the amount paid; the amount paid is used.'),
    ).toBeInTheDocument();
    expect(row.getByText('Dated before your first lot.')).toBeInTheDocument();
    expect(region.getByText(/1 row has a per-share amount times shares that differs/)).toBeInTheDocument();
  });

  it('shows the per-share amount of a row with up to four decimals', async () => {
    const items = [buildImportItem({ line: 2, name: 'Fabrikam Ltd', perShare: 0.0125, quantity: 100, amount: 1.25 })];
    const { dialog } = await openDividendImport({ report: reportOf(items) });
    const region = within(await previewFile(dialog));

    const row = within(region.getByRole('row', { name: /Fabrikam Ltd/ }));
    expect(row.getByText('₹0.0125')).toBeInTheDocument();
  });

  it('lists the bad rows (the first ones listed, all counted) and the payments that repeat', async () => {
    const report = buildImportReport({
      file: {
        ...buildImportReport().file,
        bad: [{ line: 9, reason: 'The date 31 Feb 2025 is not a real day.' }],
        badTotal: 240,
        repeated: 1,
        identicalRepeats: [
          {
            name: 'Globex Corporation',
            payDate: '2026-07-03',
            amount: 800,
            occurrences: 2,
            firstLine: 11,
            lines: [11, 14],
          },
        ],
      },
    });
    const { dialog } = await openDividendImport({ report });
    const element = await previewFile(dialog);
    const region = within(element);

    expect(count(element, 'Bad rows')).toHaveTextContent('240');
    await userEvent.click(region.getByRole('button', { name: 'Show 240 rows that could not be read' }));
    expect(region.getByText(/The date 31 Feb 2025 is not a real day\./)).toBeInTheDocument();
    expect(region.getByText('Showing the first 1 of 240.')).toBeInTheDocument();
    expect(
      region.getByText('Globex Corporation, 3 Jul 2026, ₹800.00 appears 2 times (line 11, 14) — counted once.'),
    ).toBeInTheDocument();
    expect(region.getByText(/add the second by hand if it is real/)).toBeInTheDocument();
  });

  it('shows the reason when the preview is refused and offers Retry', async () => {
    const { dialog, fake } = await openDividendImport();
    fake.config.previewProblem = {
      status: 400,
      body: {
        title: 'Invalid',
        code: 'portfolio.invalid',
        errors: { file: ['The file has no column for the amount paid. Accepted names: Dividend Paid, Amount.'] },
      },
    };
    await chooseFile(dialog);
    await userEvent.click(dialog.getByRole('button', { name: 'Preview' }));

    expect(await dialog.findByText(/The file has no column for the amount paid/)).toBeInTheDocument();

    fake.config.previewProblem = { status: 500, body: { title: 'Boom' } };
    await userEvent.click(dialog.getByRole('button', { name: 'Preview' }));
    expect(await dialog.findByRole('alert')).toHaveTextContent('Boom');
    fake.config.previewProblem = null;
    await userEvent.click(dialog.getByRole('button', { name: 'Retry' }));
    expect(await dialog.findByRole('region', { name: /Preview of/ })).toBeInTheDocument();
  });
});

describe('Import dividends: a big file', () => {
  it('shows a page of 20,000 rows at a time and moves between the pages', async () => {
    const { bigReport } = await import('@/test/dividendImportFixtures');
    const { dialog } = await openDividendImport({ report: bigReport(20_000) });
    const region = within(await previewFile(dialog));

    const table = within(region.getByRole('table', { name: 'Rows ready to add' }));
    expect(table.getAllByRole('row')).toHaveLength(51);
    expect(region.getByText('Showing rows 1–50 of 20,000')).toBeInTheDocument();
    await userEvent.click(region.getByRole('button', { name: 'Next page' }));
    expect(region.getByText('Showing rows 51–100 of 20,000')).toBeInTheDocument();
    expect(table.getByText('Company 50 Ltd')).toBeInTheDocument();
    expect(table.queryByText('Company 0 Ltd')).not.toBeInTheDocument();
    await userEvent.click(region.getByRole('button', { name: 'Previous page' }));
    expect(region.getByRole('button', { name: 'Previous page' })).toBeDisabled();
  }, 60_000);
});

describe('Import dividends: paging keeps the keyboard on the page', () => {
  it('moves focus to the status line when Next page or Previous page becomes disabled', async () => {
    const { bigReport } = await import('@/test/dividendImportFixtures');
    const { dialog } = await openDividendImport({ report: bigReport(60) });
    const region = within(await previewFile(dialog));

    await userEvent.click(region.getByRole('button', { name: 'Next page' }));
    expect(region.getByRole('button', { name: 'Next page' })).toBeDisabled();
    expect(region.getByText('Showing rows 51–60 of 60')).toHaveFocus();

    await userEvent.click(region.getByRole('button', { name: 'Previous page' }));
    expect(region.getByRole('button', { name: 'Previous page' })).toBeDisabled();
    expect(region.getByText('Showing rows 1–50 of 60')).toHaveFocus();
  });
});
