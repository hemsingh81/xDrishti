import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { csvFile, stubUploads } from '@/test/multipart';
import { buildDpPreview } from '@/test/reportImportFixtures';
import { openDividendImport, chooseFile } from './dividendImportTesting';
import { openImportFiles } from './reportImportTesting';
import {
  renderPortfolio,
  serveReportImport,
  serveTrades,
  tradesPanel,
  warmDividends,
  warmReportImport,
} from './testing';

stubUploads();

beforeAll(async () => {
  await warmDividends();
  await warmReportImport();
}, 60_000);

const wrongKind = (detected: string) => ({
  status: 400,
  body: { title: 'This is not the kind of file this import reads.', code: 'portfolio.wrong_file_kind', detected },
});

const reports = [
  [
    'global_transaction_report',
    /This is a Dhan global transaction report: Import files reads its sides, prices and charges/,
  ],
  ['dp_charges', /This is a depository \(DP\) charges sheet: Import files stores its charges as costs/],
  ['demat_holding', /This is a demat holding statement: Import files checks it against your holdings/],
  ['pnl_report', /This is a profit and loss statement: Import files recognises it, and does not import it/],
] as const;

describe('A Dhan report dropped on the trade import', () => {
  it.each(reports)('is recognised as %s, with a button that opens Import files', async (kind, text) => {
    const trades = serveTrades();
    serveReportImport({ previews: [buildDpPreview()] });
    trades.config.previewProblem = wrongKind(kind);
    const { router } = renderPortfolio('?trades=import');
    const panel = within(await tradesPanel());
    await userEvent.upload(panel.getByLabelText('Tradebook file (CSV)'), csvFile('Bill No,Date\n', 'report.csv'));
    await userEvent.click(panel.getByRole('button', { name: 'Preview' }));

    const notice = (await panel.findByText(text)).closest<HTMLElement>('[role="alert"]');
    if (!notice) throw new Error('no notice');
    expect(notice).toHaveTextContent('Nothing was imported here.');
    await userEvent.click(within(notice).getByRole('button', { name: 'Import files' }));

    const dialog = within(await screen.findByRole('dialog', { name: 'Import files' }));
    expect(dialog.getByLabelText('Report files (CSV, XLS or XLSX)')).toBeInTheDocument();
    expect(router.state.location.search).toContain('import=open');
  });
});

describe('A Dhan report dropped on the dividend import', () => {
  it.each(reports)('is recognised as %s, with a button that opens Import files', async (kind, text) => {
    const { dialog, fake } = await openDividendImport();
    serveReportImport();
    serveTrades();
    fake.config.previewProblem = wrongKind(kind);
    await chooseFile(dialog);
    await userEvent.click(dialog.getByRole('button', { name: 'Preview' }));

    expect(await dialog.findByText(text)).toBeInTheDocument();
    await userEvent.click(dialog.getByRole('button', { name: 'Import files' }));

    expect(await screen.findByRole('dialog', { name: 'Import files' })).toBeInTheDocument();
  });
});

describe('Import files: the link', () => {
  it('is taken out of the address when the dialog is closed', async () => {
    const { router } = await openImportFiles();
    expect(router.state.location.search).toBe('?import=open');

    await userEvent.click(screen.getByRole('button', { name: 'Close' }));

    await within(document.body).findByRole('button', { name: 'Sync now' });
    expect(router.state.location.search).toBe('');
  });
});
