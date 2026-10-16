import { holdingIds } from '@/test/fixtures';
import {
  buildBill,
  buildBillName,
  buildCostImportResult,
  buildDematPreview,
  buildDpPreview,
  buildFileResult,
  buildGtrPreview,
  buildUnsupportedPreview,
} from '@/test/reportImportFixtures';
import { buildBatch, buildImportResult } from '@/test/tradeFixtures';
import { addReportFiles, validateReportFile } from './reportFiles';
import {
  choiceOfDraft,
  choicesOf,
  importSummaryText,
  isIsin,
  kindLabel,
  keepDrafts,
  openNameGroups,
  spellingsLine,
  warningText,
  willChange,
  type BillDraft,
} from './reportImport';

const file = (name: string, size = 10) => ({ name, size }) as File;

describe('reportFiles', () => {
  it('accepts CSV, XLS and XLSX by their ending, whatever the case', () => {
    for (const name of ['a.csv', 'B.CSV', 'dp.xls', 'DP.XLSX']) expect(validateReportFile(file(name))).toBeUndefined();
    // The API recognises the format from the content: text and no extension at all are fine, other kinds are not.
    for (const name of ['a.txt', 'statement', '.hidden']) expect(validateReportFile(file(name))).toBeUndefined();
    expect(validateReportFile(file('a.pdf'))).toBe('“a.pdf” is not a CSV, XLS or XLSX file.');
    expect(validateReportFile(file('statement', 0))).toBe('“statement” is empty.');
    expect(validateReportFile(file('a.csv', 0))).toBe('“a.csv” is empty.');
  });

  it('keeps a file once and refuses what is over the limits, saying which', () => {
    const first = addReportFiles([], [file('a.csv'), file('a.csv'), file('b.pdf')]);
    expect(first.files.map((f) => f.name)).toEqual(['a.csv']);
    expect(first.problems).toEqual(['“b.pdf” is not a CSV, XLS or XLSX file.']);

    const many = addReportFiles(
      [],
      Array.from({ length: 22 }, (_, n) => file(`f${String(n)}.csv`)),
    );
    expect(many.files).toHaveLength(20);
    expect(many.problems).toHaveLength(2);
  });
});

describe('reportImport', () => {
  it('names the kinds in plain words and shows an unknown kind as it came', () => {
    expect(kindLabel('global_transaction_report')).toBe('Transaction report');
    expect(kindLabel('pnl_report')).toBe('Profit and loss statement');
    expect(kindLabel('brand_new_kind')).toBe('brand_new_kind');
  });

  it('turns a warning into words, with its count, and falls back to the server’s text for a new code', () => {
    expect(warningText({ code: 'needs_match', message: 'x', count: 1 }, 'Main')).toMatch(/^1 name could not be tied/);
    expect(warningText({ code: 'bad_rows', message: 'x', count: 2 }, 'Main')).toBe(
      '2 rows could not be read and are left out.',
    );
    expect(warningText({ code: 'other_account', message: 'x' }, 'Main')).toMatch(/not Main\.$/);
    expect(warningText({ code: 'something_new', message: 'The server says so.' }, 'Main')).toBe('The server says so.');
  });

  it('says a typed ISIN is valid only with twelve characters in the ISIN shape', () => {
    expect(isIsin('INE0TS001018')).toBe(true);
    expect(isIsin(' ine0ts001018 ')).toBe(true);
    expect(isIsin('INE0TS00101')).toBe(false);
    expect(isIsin('1NE0TS001018')).toBe(false);
  });

  it('sends a decision as a holding, an instrument or an ISIN, in a stable order', () => {
    const drafts = new Map<string, BillDraft>([
      ['b', { key: 'b', target: { kind: 'instrument', id: 'i-1', label: 'X' }, remember: false }],
      ['a', { key: 'a', target: { kind: 'holding', id: holdingIds.infy, label: 'INFY' }, remember: true }],
      ['c', { key: 'c', target: { kind: 'isin', isin: 'INE0TS001018' }, remember: true }],
    ]);

    expect(choicesOf(drafts)).toEqual([
      { name: 'a', holdingId: holdingIds.infy, remember: true },
      { name: 'b', instrumentId: 'i-1', remember: false },
      { name: 'c', isin: 'INE0TS001018', remember: true },
    ]);
    expect(choiceOfDraft({ key: 'z', target: { kind: 'isin', isin: 'INE0TS001018' }, remember: false })).toEqual({
      name: 'z',
      isin: 'INE0TS001018',
      remember: false,
    });
  });

  it('keeps only the decisions for names that the fresh previews still ask about', () => {
    const drafts = new Map<string, BillDraft>([
      ['orion steel', { key: 'orion steel', target: { kind: 'isin', isin: 'INE0TS001018' }, remember: true }],
      ['gone', { key: 'gone', target: { kind: 'isin', isin: 'INE0TS002016' }, remember: true }],
    ]);

    expect([...keepDrafts([buildGtrPreview(), null, buildDpPreview()], drafts).keys()]).toEqual(['orion steel']);
  });

  it('groups the open names by name key and lists up to three spellings', () => {
    const groups = openNameGroups(buildBill());

    expect(groups.map((g) => [g.key, g.spellings, g.rows, g.trades])).toEqual([
      ['orion steel', ['ORION STEEL LTD.', 'Orion Steel'], 5, 5],
      ['nova oxides chem', ['NOVA OXIDES & CHEM LTD'], 1, 1],
    ]);
    expect(spellingsLine(['a', 'b', 'c'])).toBe('a, b, c');
    expect(spellingsLine(['a', 'b', 'c', 'd', 'e'])).toBe('a, b, c +2 more');
    expect(buildBillName().candidates).toEqual([]);
  });

  it('says a file changes something when it would store or learn something', () => {
    expect(willChange(buildGtrPreview())).toBe(true);
    expect(willChange(buildDpPreview())).toBe(true);
    expect(willChange(buildDematPreview())).toBe(true);
    expect(
      willChange(buildDematPreview({ demat: { ...buildDematPreview().demat!, names: { known: 6, new: 0 } } })),
    ).toBe(false);
    expect(willChange(buildUnsupportedPreview('pnl_report', 'p.csv', null))).toBe(false);
    expect(willChange(buildGtrPreview({ supported: false }))).toBe(false);
  });

  it('sums up an import in one line for the toast', () => {
    expect(importSummaryText([])).toBe('Nothing was imported.');
    expect(
      importSummaryText([
        buildFileResult({ trades: buildImportResult({ batch: buildBatch(), imported: 3, rebuilt: false }) }),
        buildFileResult({ status: 'nothing_new' }),
        buildFileResult({ status: 'rejected' }),
      ]),
    ).toBe('Imported 3 trades. Your lots were not changed. 1 file had nothing new. 1 file not imported.');
    expect(importSummaryText([buildFileResult({ costs: buildCostImportResult() })])).toBe(
      'Imported 1 charge stored (₹12.50); 1 line in the ledger already.',
    );
  });
});
