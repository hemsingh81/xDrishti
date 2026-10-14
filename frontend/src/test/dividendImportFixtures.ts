import type {
  DividendBatch,
  DividendBatchPage,
  DividendImportResult,
  DividendMatch,
  DividendPreviewItem,
  DividendPreviewReport,
  DividendUndoResult,
} from '@/shared/api';
import { holdingIds, ids } from './fixtures';

const uuid = (n: number) => `00000000-0000-4000-8000-0000000016${String(n).padStart(2, '0')}`;

export const dividendBatchIds = { first: uuid(1), second: uuid(2), third: uuid(3) } as const;
const instrument = uuid(50);

/** The key the server gives a name: case and punctuation do not tell two spellings of one company apart. */
export const nameKeyOf = (name: string): string =>
  name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim();

/** A row that is new and matched to RELIANCE by name: 100 shares at ₹12, ₹1,200 on 5 Jun 2025. Anonymised: made-up names. */
export const buildImportItem = (overrides: Partial<DividendPreviewItem> = {}): DividendPreviewItem => {
  const item: DividendPreviewItem = {
    line: 2,
    payDate: '2025-06-05',
    name: 'Reliance Industries Ltd',
    nameKey: '',
    perShare: 12,
    quantity: 100,
    amount: 1_200,
    status: 'new',
    match: {
      kind: 'name',
      holdingId: holdingIds.reliance,
      instrumentId: instrument,
      symbol: 'RELIANCE',
      isin: 'INE002A01018',
      name: 'Reliance Industries',
      candidates: [],
    },
    warnings: [],
    ...overrides,
  };
  return { ...item, nameKey: overrides.nameKey ?? nameKeyOf(item.name) };
};

const noMatch: DividendMatch = {
  kind: 'none',
  holdingId: null,
  instrumentId: null,
  symbol: null,
  isin: null,
  name: null,
  candidates: [],
};

/** One row of each status: ready (by name), ready (part of the name), two rows of a name nothing fits, an ambiguous name, a possible duplicate and an imported one. */
export const mixedItems = (): DividendPreviewItem[] => [
  buildImportItem({ line: 2 }),
  buildImportItem({
    line: 3,
    payDate: '2025-07-10',
    name: 'Tata Consultancy',
    perShare: 10,
    quantity: 10,
    amount: 100,
    match: {
      kind: 'contains',
      holdingId: holdingIds.tcs,
      instrumentId: null,
      symbol: 'TCS',
      isin: 'INE467B01029',
      name: 'Tata Consultancy Services',
      candidates: [],
    },
    warnings: ['partial_name_match'],
  }),
  buildImportItem({
    line: 4,
    payDate: '2025-03-01',
    name: 'Northwind Traders Ltd',
    perShare: 2,
    quantity: 50,
    amount: 100,
    status: 'needs_match',
    match: { ...noMatch },
  }),
  buildImportItem({
    line: 5,
    payDate: '2025-09-01',
    name: 'Northwind Traders Ltd',
    perShare: 3,
    quantity: 50,
    amount: 150,
    status: 'needs_match',
    match: { ...noMatch },
  }),
  buildImportItem({
    line: 6,
    payDate: '2025-08-01',
    name: 'Contoso Holdings',
    perShare: 1,
    quantity: 200,
    amount: 200,
    status: 'ambiguous',
    match: {
      ...noMatch,
      candidates: [
        { holdingId: holdingIds.infy, symbol: 'INFY', name: 'Infosys' },
        { holdingId: holdingIds.tcs, symbol: 'TCS', name: 'Tata Consultancy Services' },
      ],
    },
  }),
  buildImportItem({ line: 7, payDate: '2025-12-05', status: 'possible_duplicate' }),
  buildImportItem({
    line: 8,
    payDate: '2024-12-05',
    name: 'Infosys Ltd',
    perShare: 20,
    quantity: 25,
    amount: 500,
    status: 'imported',
    match: {
      kind: 'alias',
      holdingId: holdingIds.infy,
      instrumentId: null,
      symbol: 'INFY',
      isin: 'INE009A01021',
      name: 'Infosys',
      candidates: [],
    },
  }),
];

/** The summary and totals that fit a list of rows (what the API computes). */
export function reportOf(
  items: readonly DividendPreviewItem[],
  overrides: Partial<DividendPreviewReport> = {},
): DividendPreviewReport {
  const count = (status: string) => items.filter((item) => item.status === status).length;
  const toAdd = items.filter((item) => ['new', 'needs_match', 'ambiguous'].includes(item.status));
  const years = new Map<number, { count: number; amount: number }>();
  for (const item of toAdd) {
    const year = Number(item.payDate.slice(0, 4));
    const entry = years.get(year) ?? { count: 0, amount: 0 };
    years.set(year, { count: entry.count + 1, amount: entry.amount + item.amount });
  }
  const amount = toAdd.reduce((sum, item) => sum + item.amount, 0);
  return {
    format: 'dividends',
    file: {
      fileName: 'dividends-sample.csv',
      rows: items.length,
      parsed: items.length,
      parsedAmount: items.reduce((sum, item) => sum + item.amount, 0),
      repeated: 0,
      distinct: items.length,
      identicalRepeats: [],
      bad: [],
      badTotal: 0,
      alreadyImported: count('imported'),
      possibleDuplicates: count('possible_duplicate'),
      toAdd: toAdd.length,
      needsMatch: count('needs_match'),
      ambiguous: count('ambiguous'),
    },
    items: [...items],
    totals: {
      count: toAdd.length,
      amount,
      byYear: [...years].sort(([a], [b]) => a - b).map(([year, entry]) => ({ year, ...entry })),
    },
    beforeFirstLot: { count: 0, amount: 0, firstLotDate: '2025-01-02' },
    digest: 'dividend-digest-1',
    ...overrides,
  };
}

export const buildImportReport = (overrides: Partial<DividendPreviewReport> = {}): DividendPreviewReport =>
  reportOf(mixedItems(), overrides);

/** A file of `n` rows, all new and matched (for the size test): made-up names, one per row. */
export function bigReport(n: number): DividendPreviewReport {
  return reportOf(
    Array.from({ length: n }, (_, i) =>
      buildImportItem({
        line: i + 2,
        payDate: `202${String(i % 6)}-0${String((i % 9) + 1)}-1${String(i % 9)}`,
        name: `Company ${String(i)} Ltd`,
        amount: 100 + (i % 50),
      }),
    ),
  );
}

export const buildDividendBatch = (overrides: Partial<DividendBatch> = {}): DividendBatch => ({
  id: dividendBatchIds.first,
  accountId: ids.main,
  accountName: 'Test Dhan Main',
  fileName: 'dividends-sample.csv',
  sha256: 'b'.repeat(64),
  parsed: 7,
  imported: 5,
  duplicates: 1,
  bad: 0,
  amount: 1_750,
  status: 'imported',
  activeDividends: 5,
  createdBy: 'owner',
  createdAt: '2026-10-12T09:30:00Z',
  undoneAt: null,
  ...overrides,
});

export const buildDividendBatchPage = (
  items: readonly DividendBatch[],
  overrides: Partial<DividendBatchPage> = {},
): DividendBatchPage => ({ items: [...items], page: 1, pageSize: 50, total: items.length, ...overrides });

export const buildDividendImportResult = (overrides: Partial<DividendImportResult> = {}): DividendImportResult => ({
  batch: buildDividendBatch(),
  imported: 5,
  amount: 1_750,
  allocated: 3,
  instrumentOnly: 1,
  unallocated: 1,
  alreadyImported: 1,
  possibleDuplicatesSkipped: 1,
  aliasesSaved: 0,
  ...overrides,
});

export const buildDividendUndoResult = (overrides: Partial<DividendUndoResult> = {}): DividendUndoResult => ({
  batch: buildDividendBatch({ status: 'undone', activeDividends: 0, undoneAt: '2026-10-13T08:00:00Z' }),
  alreadyUndone: false,
  voided: 5,
  ...overrides,
});
