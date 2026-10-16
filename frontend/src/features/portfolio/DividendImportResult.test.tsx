import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildImportItem, reportOf } from '@/test/dividendImportFixtures';
import { stubUploads } from '@/test/multipart';
import { server } from '@/test/server';
import { openDividendImport, previewFile, type Panel } from './dividendImportTesting';
import { warmDividends } from './testing';

stubUploads();

beforeAll(warmDividends, 60_000);

const importButton = (dialog: Panel) => dialog.getByRole('button', { name: /^Import \d+ dividends$/ });

describe('Import dividends: the result', () => {
  it('stores the file, says what was done in numbers and in a toast, and moves the keyboard to the result', async () => {
    const { dialog, fake } = await openDividendImport();
    await previewFile(dialog);

    await userEvent.click(importButton(dialog));

    const result = within(await dialog.findByRole('region', { name: 'Import result' }));
    expect(result.getByRole('heading', { name: 'Import done' })).toHaveFocus();
    expect(result.getByText('5 dividends stored (₹1,750.00).')).toBeInTheDocument();
    const figure = (label: string) => result.getByText(label, { selector: 'dt' }).nextElementSibling;
    expect(figure('Imported')).toHaveTextContent('5');
    expect(figure('Allocated to a holding')).toHaveTextContent('3');
    expect(figure('Instrument only')).toHaveTextContent('1');
    expect(figure('Unallocated')).toHaveTextContent('1');
    expect(figure('Already imported')).toHaveTextContent('1');
    expect(figure('Duplicates skipped')).toHaveTextContent('1');
    expect(figure('Matches remembered')).toHaveTextContent('0');
    // The toast sits outside the modal dialog, so it is found by its text rather than as a region.
    expect(await screen.findByText('Imported 5 dividends (₹1,750.00).')).toBeInTheDocument();
    expect(fake.calls.imports[0]?.upload.fields.digest).toBe('dividend-digest-1');
    expect(dialog.queryByRole('region', { name: /Preview of/ })).not.toBeInTheDocument();
  });

  it('says so when nothing was new', async () => {
    const { dialog, fake } = await openDividendImport();
    fake.config.report = reportOf([buildImportItem({ status: 'imported' })]);
    await previewFile(dialog);
    expect(dialog.getByRole('button', { name: 'Import' })).toBeDisabled();
    expect(dialog.getByText('There is nothing new to import.')).toBeInTheDocument();
  });

  it('imports again after "Import another file"', async () => {
    const { dialog } = await openDividendImport();
    await previewFile(dialog);
    await userEvent.click(importButton(dialog));
    await userEvent.click(await dialog.findByRole('button', { name: 'Import another file' }));

    expect(dialog.queryByRole('region', { name: 'Import result' })).not.toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Preview' })).toBeDisabled();
  });

  it('reads the dividend lists, the income figures and nothing else again', async () => {
    const asked: string[] = [];
    server.events.on('request:start', ({ request }) => {
      asked.push(`${request.method} ${new URL(request.url).pathname}`);
    });
    const { dialog } = await openDividendImport();
    await previewFile(dialog);
    asked.length = 0;

    await userEvent.click(importButton(dialog));
    await dialog.findByText('Import done');

    await waitFor(() => {
      expect(asked).toContain('GET /api/holdings/summary');
    });
    expect(asked).toContain('GET /api/accounts/00000000-0000-4000-8000-000000000001/dividends');
    expect(asked.some((entry) => entry.includes('/api/portfolio/history'))).toBe(false);
    expect(asked.some((entry) => entry.includes('/trades'))).toBe(false);
    server.events.removeAllListeners();
  });
});

describe('Import dividends: undo from the result', () => {
  it('asks first, voids the batch, says so in a toast and takes the button away', async () => {
    const { dialog, fake } = await openDividendImport();
    await previewFile(dialog);
    await userEvent.click(importButton(dialog));
    await userEvent.click(await dialog.findByRole('button', { name: 'Undo this import' }));

    const ask = within(await screen.findByRole('dialog', { name: 'Undo this import?' }));
    expect(ask.getByText(/5 dividends of “dividends-sample\.csv” will be voided/)).toBeInTheDocument();
    await userEvent.click(ask.getByRole('button', { name: 'Undo import' }));

    expect(await screen.findByText('Import undone: 5 dividends voided.')).toBeInTheDocument();
    expect(await dialog.findByText(/This import was undone: its 5 dividends no longer count\./)).toBeInTheDocument();
    expect(dialog.queryByRole('button', { name: 'Undo this import' })).not.toBeInTheDocument();
    expect(fake.calls.undone).toHaveLength(1);
    await waitFor(() => {
      expect(dialog.getByText(/This import was undone/)).toHaveFocus();
    });
  });

  it('keeps the question open and says why when the undo fails', async () => {
    const { dialog, fake } = await openDividendImport();
    await previewFile(dialog);
    await userEvent.click(importButton(dialog));
    fake.config.undoProblem = { status: 500, body: { title: 'The database is busy.' } };
    await userEvent.click(await dialog.findByRole('button', { name: 'Undo this import' }));
    const ask = within(await screen.findByRole('dialog', { name: 'Undo this import?' }));

    await userEvent.click(ask.getByRole('button', { name: 'Undo import' }));

    expect(await ask.findByRole('alert')).toHaveTextContent('The database is busy.');
    expect(dialog.queryByText(/This import was undone/)).not.toBeInTheDocument();
  });
});

describe('Import dividends: when the import is refused', () => {
  it('shows the explanation of a conflict and keeps the preview', async () => {
    const { dialog, fake } = await openDividendImport();
    await previewFile(dialog);
    fake.config.importProblems.push({ status: 409, body: { title: 'Conflict', code: 'portfolio.conflict' } });

    await userEvent.click(importButton(dialog));

    expect(await dialog.findByRole('alert')).toHaveTextContent(/The dividends changed while this was being done/);
    expect(dialog.getByRole('region', { name: /Preview of/ })).toBeInTheDocument();
    await userEvent.click(importButton(dialog));
    expect(await dialog.findByText('Import done')).toBeInTheDocument();
  });

  it('shows what the API said about the choices', async () => {
    const { dialog, fake } = await openDividendImport();
    await previewFile(dialog);
    fake.config.importProblems.push({
      status: 400,
      body: {
        title: 'Invalid',
        code: 'portfolio.invalid',
        errors: { choices: ['A choice names a holding of another account.'] },
      },
    });

    await userEvent.click(importButton(dialog));

    expect(await dialog.findByRole('alert')).toHaveTextContent('A choice names a holding of another account.');
  });
});

describe('Import dividends: the preview went stale', () => {
  const fresh = () =>
    reportOf(
      [
        buildImportItem({ line: 2 }),
        buildImportItem({
          line: 3,
          name: 'Northwind Traders Ltd',
          status: 'needs_match',
          amount: 100,
          match: {
            kind: 'none',
            holdingId: null,
            instrumentId: null,
            symbol: null,
            isin: null,
            name: null,
            candidates: [],
          },
        }),
      ],
      { digest: 'dividend-digest-2' },
    );

  it('swaps in the fresh preview with a warning, keeps the choices that still apply, and asks to confirm again', async () => {
    const { dialog, fake } = await openDividendImport();
    const region = within(await previewFile(dialog));
    await userEvent.click(region.getByRole('button', { name: 'Choose a holding for Northwind Traders Ltd' }));
    const picker = within(await screen.findByRole('dialog', { name: 'Choose where these dividends go' }));
    await userEvent.click(picker.getByRole('combobox', { name: 'Holding or instrument' }));
    await userEvent.click(await screen.findByRole('option', { name: /Leave unallocated/ }));
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));
    fake.config.importProblems.push({
      status: 409,
      body: { title: 'Stale', code: 'portfolio.preview_stale', report: fresh() },
    });

    await userEvent.click(importButton(dialog));

    const notice = await dialog.findByText(/Holdings, remembered matches or dividends changed — please review again\./);
    expect(notice.closest('[role="alert"]')).toHaveFocus();
    expect(dialog.queryByText('Import done')).not.toBeInTheDocument();
    expect(dialog.getByText('2 dividends (₹1,300.00) will be stored.')).toBeInTheDocument();
    expect(fake.calls.imports).toHaveLength(1);

    fake.config.report = fresh();
    await userEvent.click(importButton(dialog));
    await dialog.findByText('Import done');
    expect(fake.calls.imports).toHaveLength(2);
    expect(fake.calls.imports[1]?.upload.fields.digest).toBe('dividend-digest-2');
    expect(fake.calls.imports[1]?.choices).toMatchObject([{ name: 'northwind traders ltd', none: true }]);
  });

  it('keeps a choice when the fresh preview spells the name differently but the name key is the same', async () => {
    const { dialog, fake } = await openDividendImport();
    const region = within(await previewFile(dialog));
    await userEvent.click(region.getByRole('button', { name: 'Choose a holding for Northwind Traders Ltd' }));
    const picker = within(await screen.findByRole('dialog', { name: 'Choose where these dividends go' }));
    await userEvent.click(picker.getByRole('combobox', { name: 'Holding or instrument' }));
    await userEvent.click(await screen.findByRole('option', { name: /Leave unallocated/ }));
    await userEvent.click(picker.getByRole('button', { name: 'Use this match' }));
    const respelt = reportOf(
      [
        buildImportItem({ line: 2 }),
        buildImportItem({
          line: 3,
          name: 'NORTHWIND TRADERS LTD.',
          nameKey: 'northwind traders ltd',
          status: 'needs_match',
          amount: 100,
          match: {
            kind: 'none',
            holdingId: null,
            instrumentId: null,
            symbol: null,
            isin: null,
            name: null,
            candidates: [],
          },
        }),
      ],
      { digest: 'dividend-digest-2' },
    );
    fake.config.importProblems.push({
      status: 409,
      body: { title: 'Stale', code: 'portfolio.preview_stale', report: respelt },
    });

    await userEvent.click(importButton(dialog));

    const fresh = within(await dialog.findByRole('region', { name: /Preview of/ }));
    expect(await fresh.findByText('NORTHWIND TRADERS LTD.')).toBeInTheDocument();
    expect(fresh.getByText('Left unallocated')).toBeInTheDocument();
    fake.config.report = respelt;
    await userEvent.click(importButton(dialog));
    await dialog.findByText('Import done');
    expect(fake.calls.imports[1]?.choices).toMatchObject([{ name: 'northwind traders ltd', none: true }]);
  });

  it('reads the file again when the refusal brings no usable preview', async () => {
    const { dialog, fake } = await openDividendImport();
    await previewFile(dialog);
    fake.config.report = fresh();
    fake.config.importProblems.push({ status: 409, body: { title: 'Stale', code: 'portfolio.preview_stale' } });

    await userEvent.click(importButton(dialog));

    await waitFor(() => {
      expect(fake.calls.previews).toHaveLength(2);
    });
    expect(await dialog.findByText(/please review again/)).toBeInTheDocument();
    expect(dialog.getByText('2 dividends (₹1,300.00) will be stored.')).toBeInTheDocument();
  });
});
