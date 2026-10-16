import { within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { csvFile, stubUploads } from '@/test/multipart';
import type { DpLine } from '@/shared/api';
import {
  buildBill,
  buildBillName,
  buildCosts,
  buildDemat,
  buildDematPreview,
  buildDpLine,
  buildDpPreview,
  buildGtrPreview,
  buildTradeListPreview,
  buildUnknownPreview,
  buildUnsupportedPreview,
} from '@/test/reportImportFixtures';
import { statementText, withoutSidesText } from './lib/wrongFile';
import {
  cardOf,
  dematFile,
  dpFile,
  figureIn,
  gtrFile,
  openImportFiles,
  previewFiles,
  sampleCsv,
} from './reportImportTesting';
import { warmReportImport } from './testing';

stubUploads();

beforeAll(warmReportImport, 60_000);

describe('Import files: a global transaction report', () => {
  it('shows the kind in plain words, the counts and the period', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);

    const card = cardOf(dialog, 'global-transaction-fy26.csv');
    expect(card.getByText('Transaction report')).toBeInTheDocument();
    expect(figureIn(card, 'Rows read')).toHaveTextContent('40');
    expect(figureIn(card, 'Valid')).toHaveTextContent('34');
    expect(figureIn(card, 'To add')).toHaveTextContent('30');
    expect(figureIn(card, 'Already imported')).toHaveTextContent('2');
    expect(figureIn(card, 'Bad rows')).toHaveTextContent('1');
    expect(card.getByText(/Period:/)).toHaveTextContent('Period: 1 Apr 2025 to 31 Mar 2026');
  });

  it('says the notices in plain words', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);

    const notices = within(
      cardOf(dialog, 'global-transaction-fy26.csv').getByRole('list', { name: 'Notices about this file' }),
    );
    expect(
      notices.getByText(
        /3 names could not be tied to a security\. Their trades are kept without an ISIN until you choose a match/,
      ),
    ).toBeInTheDocument();
    expect(notices.getByText(/4 trades are left out because Dhan’s own trade history already has/)).toBeInTheDocument();
  });

  it('asks about the names nothing fits, one line per name with its spellings', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);

    const card = cardOf(dialog, 'global-transaction-fy26.csv');
    expect(card.getByText(/5 names in the file; 2 matched to a security, 3 need a match/)).toBeInTheDocument();
    const table = within(card.getByRole('table', { name: 'Names that need a match' }));
    const orion = within(table.getByRole('row', { name: /ORION STEEL LTD\./ }));
    expect(orion.getByText('Orion Steel')).toBeInTheDocument();
    expect(orion.getAllByText('5')).toHaveLength(2);
    expect(orion.getByText('Not chosen: kept without an ISIN')).toBeInTheDocument();
    const nova = within(table.getByRole('row', { name: /NOVA OXIDES/ }));
    expect(nova.getByText('Ambiguous: fits NOVAOX, NOVAOY')).toBeInTheDocument();
    expect(table.getAllByRole('row')).toHaveLength(3);
  });

  it('lists up to three spellings of a name and then how many more', async () => {
    const spellings = ['ACME ENGG LTD', 'Acme Engg', 'ACME ENGG. LTD.', 'Acme Engg Ltd', 'ACME  ENGG'];
    const bill = buildBill({
      names: spellings.map((name) =>
        buildBillName({
          name,
          nameKey: 'acme engg',
          status: 'needs_match',
          kind: 'none',
          isin: null,
          rows: 1,
          trades: 1,
        }),
      ),
      namesTotal: 5,
      needsMatch: 5,
    });
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview({ bill })] });
    await previewFiles(dialog, [gtrFile()]);

    const table = within(
      cardOf(dialog, 'global-transaction-fy26.csv').getByRole('table', { name: 'Names that need a match' }),
    );
    expect(table.getByText('ACME ENGG LTD')).toBeInTheDocument();
    expect(table.getByText('ACME ENGG. LTD.')).toBeInTheDocument();
    expect(table.queryByText('Acme Engg Ltd')).not.toBeInTheDocument();
    expect(table.getByText('+2 more')).toBeInTheDocument();
    expect(table.getAllByRole('row')).toHaveLength(2);
  });

  it('lists the matched names behind a button, with how each was matched', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);

    const card = cardOf(dialog, 'global-transaction-fy26.csv');
    await userEvent.click(card.getByRole('button', { name: 'Show the 2 matched names' }));
    const table = within(card.getByRole('table', { name: 'Matched names' }));
    expect(table.getByText('INFOSYS LTD')).toBeInTheDocument();
    expect(table.getByText('symbol')).toBeInTheDocument();
    expect(table.getByText('part of the name: check it')).toBeInTheDocument();
  });

  it('counts the rows left out by reason', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);

    const reasons = within(
      cardOf(dialog, 'global-transaction-fy26.csv').getByRole('list', { name: '8 rows are left out, by reason:' }),
    );
    expect(reasons.getByText('Covered by Dhan: 4')).toBeInTheDocument();
    expect(reasons.getByText('Futures and options: 3')).toBeInTheDocument();
    expect(reasons.getByText('Commodity (MCX): 1')).toBeInTheDocument();
  });

  it('shows what Dhan covers and the days whose quantity differs', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);

    const card = cardOf(dialog, 'global-transaction-fy26.csv');
    expect(
      card.getByText(/4 trades of the report are left out: Dhan’s own trade history has the same day/),
    ).toBeInTheDocument();
    const table = within(card.getByRole('table', { name: 'Quantity differences from Dhan' }));
    const row = within(table.getByRole('row', { name: /12 Jun 2025/ }));
    expect(row.getByText('INFY (INE009A01021)')).toBeInTheDocument();
    expect(row.getByText('Buy')).toBeInTheDocument();
    expect(row.getByText('12')).toBeInTheDocument();
    expect(row.getByText('10')).toBeInTheDocument();
  });

  it('says whether the charges match the report’s own total', async () => {
    const { dialog } = await openImportFiles({
      previews: [
        buildGtrPreview(),
        buildGtrPreview({
          fileName: 'global-transaction-fy25.csv',
          bill: buildBill({ charges: { total: 100, reportTotal: 99, matches: false } }),
        }),
      ],
    });
    await previewFiles(dialog, [gtrFile(), csvFile(sampleCsv, 'global-transaction-fy25.csv')]);

    expect(
      cardOf(dialog, 'global-transaction-fy26.csv').getByText(
        /₹1,234\.56, which matches the report’s own total \(₹1,234\.56\)/,
      ),
    ).toBeInTheDocument();
    expect(
      cardOf(dialog, 'global-transaction-fy25.csv').getByText(
        /₹100\.00, which does not match the report’s own total \(₹99\.00\)/,
      ),
    ).toBeInTheDocument();
  });

  it('shows the securities with the rebuild status, as the trade import does', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);

    const table = within(
      cardOf(dialog, 'global-transaction-fy26.csv').getByRole('table', { name: 'Securities in the file' }),
    );
    const infy = within(table.getByRole('row', { name: /INFY/ }));
    expect(infy.getByText('OK')).toBeInTheDocument();
    expect(table.getByRole('row', { name: /TCS/ })).toBeInTheDocument();
  });

  it('lists the rows that could not be read', async () => {
    const { dialog } = await openImportFiles({ previews: [buildGtrPreview()] });
    await previewFiles(dialog, [gtrFile()]);

    const card = cardOf(dialog, 'global-transaction-fy26.csv');
    await userEvent.click(card.getByRole('button', { name: 'Show 1 row that could not be read' }));
    expect(card.getByText(/The date 31\/02\/2026 is not a real day/)).toBeInTheDocument();
  });
});

const lines = (n: number): DpLine[] =>
  Array.from({ length: n }, (_, i) => buildDpLine({ line: i + 1, name: `Security ${String(i + 1)}` }));

describe('Import files: depository charges', () => {
  it('counts the lines, the amount to store and the names it teaches', async () => {
    const { dialog } = await openImportFiles({ previews: [buildDpPreview()] });
    await previewFiles(dialog, [dpFile()]);

    const card = cardOf(dialog, 'dp-charges-fy26.xls');
    expect(card.getByText('DP charges', { selector: '.MuiChip-label' })).toBeInTheDocument();
    expect(figureIn(card, 'Lines read')).toHaveTextContent('4');
    expect(figureIn(card, 'To store')).toHaveTextContent('1');
    expect(figureIn(card, 'Amount to store')).toHaveTextContent('₹12.50');
    expect(figureIn(card, 'Cost nothing')).toHaveTextContent('1');
    expect(figureIn(card, 'Buy side')).toHaveTextContent('1');
    expect(figureIn(card, 'Already in the ledger')).toHaveTextContent('1');
    expect(card.getByText(/names 3 securities: 2 new to the app, 1 known/)).toBeInTheDocument();
  });

  it('lists every line with what happens to it', async () => {
    const { dialog } = await openImportFiles({ previews: [buildDpPreview()] });
    await previewFiles(dialog, [dpFile()]);

    const table = within(cardOf(dialog, 'dp-charges-fy26.xls').getByRole('table', { name: 'Depository charge lines' }));
    expect(table.getByText('Will be stored')).toBeInTheDocument();
    expect(table.getByText('Costs nothing')).toBeInTheDocument();
    expect(table.getByText('Buy side: not charged')).toBeInTheDocument();
    expect(table.getByText('Already in the ledger')).toBeInTheDocument();
    expect(table.getAllByText('INE0TS001018')).toHaveLength(4);
  });

  it('shows the lines 50 at a time and says when the API listed only some', async () => {
    const costs = buildCosts({ lines: lines(120), linesTotal: 400 });
    const { dialog } = await openImportFiles({ previews: [buildDpPreview({ costs })] });
    await previewFiles(dialog, [dpFile()]);

    const card = cardOf(dialog, 'dp-charges-fy26.xls');
    expect(card.getAllByRole('row')).toHaveLength(51);
    expect(card.getByText('Showing lines 1–50 of 120')).toBeInTheDocument();
    expect(card.getByText('Only the first 120 of 400 lines are listed.')).toBeInTheDocument();
    await userEvent.click(card.getByRole('button', { name: 'Next page' }));
    expect(card.getByText('Showing lines 51–100 of 120')).toBeInTheDocument();
    expect(card.getByText('Security 51')).toBeInTheDocument();
  });

  it('says nothing is new when every line is stored already', async () => {
    const costs = buildCosts({
      counts: { read: 4, toStore: 0, zero: 0, buySide: 0, alreadyImported: 4, alreadyInLedger: 0, offMarket: 0 },
      amountToStore: 0,
    });
    const { dialog } = await openImportFiles({ previews: [buildDpPreview({ costs })] });
    await previewFiles(dialog, [dpFile()]);

    expect(cardOf(dialog, 'dp-charges-fy26.xls').getByText(/Nothing new/)).toBeInTheDocument();
    expect(dialog.getByText('There is nothing to import.')).toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Import' })).toBeDisabled();
  });
});

describe('Import files: a demat holding statement', () => {
  it('says that nothing is changed and counts the differences', async () => {
    const { dialog } = await openImportFiles({ previews: [buildDematPreview()] });
    await previewFiles(dialog, [dematFile()]);

    const card = cardOf(dialog, 'demat-holding.csv');
    expect(card.getByText('Demat holding', { selector: '.MuiChip-label' })).toBeInTheDocument();
    expect(card.getByText(/Informational: nothing is changed/)).toBeInTheDocument();
    expect(figureIn(card, 'As of')).toHaveTextContent('31 Mar 2026');
    expect(figureIn(card, 'Securities in the statement')).toHaveTextContent('6');
    expect(figureIn(card, 'Matching your holdings')).toHaveTextContent('4');
    expect(card.getByText(/The statement and your synced holdings differ\. Nothing is changed/)).toBeInTheDocument();
  });

  it('lists the quantity differences, the securities held but not synced and the synced ones missing from the statement', async () => {
    const { dialog } = await openImportFiles({ previews: [buildDematPreview()] });
    await previewFiles(dialog, [dematFile()]);

    const card = cardOf(dialog, 'demat-holding.csv');
    const differences = within(card.getByRole('table', { name: 'Quantity differences' }));
    const row = within(differences.getByRole('row', { name: /Orion Steel Ltd/ }));
    expect(row.getByText('30')).toBeInTheDocument();
    expect(row.getByText('25')).toBeInTheDocument();
    expect(row.getByText('5')).toBeInTheDocument();
    expect(
      within(card.getByRole('table', { name: 'Held at Dhan, not synced' })).getByText('Zenith Energy System Ltd'),
    ).toBeInTheDocument();
    expect(
      within(card.getByRole('table', { name: 'Synced, not in the statement' })).getByText('SMALLCO'),
    ).toBeInTheDocument();
  });

  it('pages a long list and says how many there are in all', async () => {
    const held = Array.from({ length: 60 }, (_, i) => ({
      isin: `INE0TS9${String(i).padStart(4, '0')}`.slice(0, 11) + String(i % 10),
      name: `Held Security ${String(i + 1)}`,
      quantity: i + 1,
    }));
    const demat = buildDemat({ heldNotSynced: held, heldNotSyncedTotal: 75 });
    const { dialog } = await openImportFiles({ previews: [buildDematPreview({ demat })] });
    await previewFiles(dialog, [dematFile()]);

    const card = cardOf(dialog, 'demat-holding.csv');
    const list = within(card.getByRole('region', { name: 'Held at Dhan, not synced (75)' }));
    expect(list.getByRole('heading', { name: 'Held at Dhan, not synced (75)' })).toBeInTheDocument();
    expect(list.getByText('Showing securities 1–50 of 60')).toBeInTheDocument();
    expect(list.getByText('Only the first 60 of 75 securities are listed.')).toBeInTheDocument();
    await userEvent.click(list.getByRole('button', { name: 'Next page' }));
    expect(list.getByText('Held Security 60')).toBeInTheDocument();
  });

  it('says so when everything agrees', async () => {
    const demat = buildDemat({
      matching: 6,
      quantityDifferences: [],
      quantityDifferencesTotal: 0,
      heldNotSynced: [],
      heldNotSyncedTotal: 0,
      syncedNotInStatement: [],
      syncedNotInStatementTotal: 0,
      names: { known: 6, new: 0 },
    });
    const { dialog } = await openImportFiles({ previews: [buildDematPreview({ demat, warnings: [] })] });
    await previewFiles(dialog, [dematFile()]);

    const card = cardOf(dialog, 'demat-holding.csv');
    expect(card.getByText('Every security in both agrees on the quantity.')).toBeInTheDocument();
    expect(card.getByText('Every synced holding is in the statement.')).toBeInTheDocument();
    expect(card.getByText(/Nothing new/)).toBeInTheDocument();
  });
});

describe('Import files: files that are not imported here', () => {
  it.each([
    [
      'pnl_report',
      'pnl-fy26.csv',
      'Profit and loss statement',
      'This is a statement of results; the app does not import it.',
    ],
    ['tradebook_without_sides', 'tradebook-plain.csv', 'Tradebook without buy/sell', withoutSidesText],
    ['statement', 'statement.csv', 'Statement of amounts', statementText],
  ])('shows the message of a %s and nothing to import', async (kind, fileName, chip, message) => {
    const { dialog } = await openImportFiles({ previews: [buildUnsupportedPreview(kind, fileName, message)] });
    await previewFiles(dialog, [csvFile(sampleCsv, fileName)]);

    const card = cardOf(dialog, fileName);
    expect(card.getByText(chip)).toBeInTheDocument();
    expect(card.getByText(message)).toBeInTheDocument();
    expect(card.queryByText('Rows read')).not.toBeInTheDocument();
    expect(dialog.getByRole('button', { name: 'Import' })).toBeDisabled();
  });

  it('explains a statement of results even when the server sent no message', async () => {
    const { dialog } = await openImportFiles({
      previews: [buildUnsupportedPreview('pnl_report', 'pnl-fy26.csv', null)],
    });
    await previewFiles(dialog, [csvFile(sampleCsv, 'pnl-fy26.csv')]);

    expect(
      cardOf(dialog, 'pnl-fy26.csv').getByText(
        /a statement of results, not a list of trades\. It is recognised and not imported/,
      ),
    ).toBeInTheDocument();
  });

  it('shows the error of a file it cannot read', async () => {
    const { dialog } = await openImportFiles({ previews: [buildUnknownPreview('mystery.csv')] });
    await previewFiles(dialog, [csvFile(sampleCsv, 'mystery.csv')]);

    const card = cardOf(dialog, 'mystery.csv');
    expect(card.getByText('Unknown file')).toBeInTheDocument();
    expect(card.getByRole('alert')).toHaveTextContent('This file is not one of the reports the app reads');
  });
});

describe('Import files: a trade list', () => {
  it('shows the securities and what a rebuild would do', async () => {
    const { dialog } = await openImportFiles({ previews: [buildTradeListPreview()] });
    await previewFiles(dialog, [csvFile(sampleCsv, 'tradebook-fy26.csv')]);

    const card = cardOf(dialog, 'tradebook-fy26.csv');
    expect(card.getByText('Trade list')).toBeInTheDocument();
    expect(card.getByRole('table', { name: 'Securities in the file' })).toBeInTheDocument();
    expect(card.queryByRole('table', { name: 'Names that need a match' })).not.toBeInTheDocument();
  });
});

describe('Import files: several files at once', () => {
  it('gives each file its own card and counts the ones an import would take', async () => {
    const { dialog } = await openImportFiles({
      previews: [
        buildGtrPreview(),
        buildDpPreview(),
        buildDematPreview(),
        buildUnsupportedPreview('pnl_report', 'pnl-fy26.csv', 'A statement of results.'),
        buildUnknownPreview('mystery.csv'),
      ],
    });

    const region = await previewFiles(dialog, [
      gtrFile(),
      dpFile(),
      dematFile(),
      csvFile(sampleCsv, 'pnl-fy26.csv'),
      csvFile(sampleCsv, 'mystery.csv'),
    ]);

    expect(
      within(region).getByRole('heading', { name: 'Preview of 5 files — nothing is stored yet' }),
    ).toBeInTheDocument();
    for (const name of [
      'global-transaction-fy26.csv',
      'dp-charges-fy26.xls',
      'demat-holding.csv',
      'pnl-fy26.csv',
      'mystery.csv',
    ]) {
      expect(dialog.getByRole('region', { name })).toBeInTheDocument();
    }
    expect(dialog.getByRole('button', { name: 'Import 3 files' })).toBeEnabled();
    expect(
      dialog.getByText(
        '30 trades, 1 depository charge (₹12.50), 3 names remembered from a demat statement will be stored.',
      ),
    ).toBeInTheDocument();
  });
});
