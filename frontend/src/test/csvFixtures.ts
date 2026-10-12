import type { CsvFilePreview, CsvFileRow, CsvPreviewBar, CsvPreviewComparison } from '@/shared/api';
import { dataRowId } from './fixtures';

/** Builders for the CSV review of the Data screen (P1-07a): the folder's files and the dry run over them. */

const pending: Pick<CsvFileRow, 'rows' | 'firstBar' | 'lastBar' | 'loadedAt' | 'overlapsWith'> = {
  rows: null,
  firstBar: null,
  lastBar: null,
  loadedAt: null,
  overlapsWith: null,
};

/** A pending file that maps to one instrument. */
export const buildCsvFile = (overrides: Partial<CsvFileRow> = {}): CsvFileRow => ({
  name: 'ALPHA_1m.csv',
  state: 'pending',
  size: 2_345_678,
  modifiedAt: '2026-10-11T18:00:00+05:30',
  symbol: 'ALPHA',
  mapping: 'mapped',
  instrument: { id: dataRowId(11), symbol: 'NSE:ALPHA' },
  ...pending,
  ...overrides,
});

/** A file that was loaded and has not changed since. */
export const buildLoadedCsvFile = (overrides: Partial<CsvFileRow> = {}): CsvFileRow =>
  buildCsvFile({
    name: 'RELIANCE_1m.csv.gz',
    state: 'loaded',
    symbol: 'RELIANCE',
    instrument: { id: dataRowId(1), symbol: 'NSE:RELIANCE' },
    size: 9_876_543,
    rows: 1_234_567,
    firstBar: '2024-04-01T09:15:00+05:30',
    lastBar: '2026-04-30T15:29:00+05:30',
    loadedAt: '2026-10-12T09:10:00+05:30',
    // Other loaded files of the instrument that cover part of the same time (none by default).
    overlapsWith: [],
    ...overrides,
  });

/**
 * A mixed folder: two pending files, one changed, two loaded, and three that cannot be loaded (unknown symbol,
 * ambiguous symbol, invalid name). Pending and changed first, then loaded, by name, as the API lists them.
 */
export const sampleCsvFiles = (): CsvFileRow[] => [
  buildCsvFile(),
  buildCsvFile({
    name: 'NEWCO_1m.csv.gz',
    symbol: 'NEWCO',
    size: 1_048_576,
    instrument: { id: dataRowId(12), symbol: 'NSE:NEWCO' },
  }),
  buildCsvFile({
    name: 'ZZZZ_1m.csv',
    symbol: 'ZZZZ',
    mapping: 'unknown',
    instrument: null,
  }),
  buildCsvFile({
    name: 'DUP_1m.csv',
    symbol: 'DUP',
    mapping: 'ambiguous',
    instrument: null,
  }),
  buildCsvFile({
    name: 'my data_1m.csv',
    symbol: null,
    mapping: 'invalidName',
    instrument: null,
  }),
  buildLoadedCsvFile({
    name: 'INFY_1m.csv.gz',
    state: 'changed',
    symbol: 'INFY',
    instrument: { id: dataRowId(4), symbol: 'NSE:INFY' },
    rows: 800_000,
  }),
  buildLoadedCsvFile(),
  buildLoadedCsvFile({
    name: 'TCS_1m.csv.gz',
    symbol: 'TCS',
    instrument: { id: dataRowId(2), symbol: 'NSE:TCS' },
    rows: 900_000,
  }),
];

/** `count` loaded files (CSV000_1m.csv …), for paging. */
export const manyCsvFiles = (count: number): CsvFileRow[] =>
  Array.from({ length: count }, (_, index) =>
    buildLoadedCsvFile({
      name: `CSV${String(index).padStart(3, '0')}_1m.csv.gz`,
      symbol: `CSV${String(index).padStart(3, '0')}`,
      instrument: { id: dataRowId(index % 90), symbol: `NSE:CSV${String(index).padStart(3, '0')}` },
    }),
  );

export const buildPreviewBar = (overrides: Partial<CsvPreviewBar> = {}): CsvPreviewBar => ({
  time: '2024-04-01T09:15:00+05:30',
  open: 100,
  high: 101.5,
  low: 99.5,
  close: 101,
  volume: 12_345,
  ...overrides,
});

export const buildComparison = (overrides: Partial<CsvPreviewComparison> = {}): CsvPreviewComparison => ({
  wouldInsert: 1_200,
  wouldUpdate: 30,
  keepExistingDhan: 45,
  unchanged: 800,
  comparedRows: 2_075,
  sampled: false,
  ...overrides,
});

/** The dry run of a file that passes. */
export const buildCsvPreview = (overrides: Partial<CsvFilePreview> = {}): CsvFilePreview => ({
  name: 'ALPHA_1m.csv',
  verdict: 'pass',
  message: null,
  state: 'pending',
  symbol: 'ALPHA',
  mapping: 'mapped',
  instrument: { id: dataRowId(11), symbol: 'NSE:ALPHA' },
  wouldLoad: true,
  rows: 2_100,
  accepted: 2_075,
  repaired: 12,
  rejected: 25,
  firstBar: '2024-04-01T09:15:00+05:30',
  lastBar: '2024-04-30T15:29:00+05:30',
  tradingDays: 20,
  rejections: { 'High below low': 20, 'Volume negative': 5 },
  examples: ['Line 14: High below low', 'Line 99: Volume negative'],
  firstRows: [
    buildPreviewBar(),
    buildPreviewBar({
      time: '2024-04-01T09:16:00+05:30',
      open: 101,
      high: 102,
      low: 100.5,
      close: 101.5,
      volume: 8_000,
    }),
  ],
  comparison: buildComparison(),
  ...overrides,
});

/** The session check refused the file (its times are shifted). */
export const buildRefusedPreview = (overrides: Partial<CsvFilePreview> = {}): CsvFilePreview =>
  buildCsvPreview({
    name: 'NEWCO_1m.csv.gz',
    symbol: 'NEWCO',
    instrument: { id: dataRowId(12), symbol: 'NSE:NEWCO' },
    verdict: 'refused',
    message: 'The bars are shifted by 5 hours 30 minutes from the session.',
    wouldLoad: false,
    accepted: 0,
    tradingDays: 0,
    firstRows: [],
    comparison: null,
    ...overrides,
  });

/** The file could not be read. */
export const buildErrorPreview = (name: string, overrides: Partial<CsvFilePreview> = {}): CsvFilePreview =>
  buildCsvPreview({
    name,
    verdict: 'error',
    message: 'The file is not in the CSV folder.',
    state: null,
    symbol: null,
    mapping: null,
    instrument: null,
    wouldLoad: false,
    rows: 0,
    accepted: 0,
    repaired: 0,
    rejected: 0,
    firstBar: null,
    lastBar: null,
    tradingDays: 0,
    rejections: {},
    examples: [],
    firstRows: [],
    comparison: null,
    ...overrides,
  });
