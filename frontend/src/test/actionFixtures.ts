import type { CorporateAction, DataCoverageRow, DataNotice } from '@/shared/api';
import { dataRowId, sampleCoverage } from './fixtures';

/** Builders for the corporate actions of the Data screen (P1-10a): rows, pages, notices and the CSV end. */

export const actionId = (n: number) => `00000000-0000-4000-8000-0000000005${String(n).padStart(2, '0')}`;

export const csvEndBar = '2026-04-30T15:29:00+05:30';

/** The price multiplier the API sends: a split n:d is n/d, a bonus a:b is b/(a+b). */
export const factorFor = (kind: string, numerator: number, denominator: number): number =>
  kind === 'split' ? numerator / denominator : denominator / (numerator + denominator);

/** A suggestion found by the detection: Infosys split 1:5 on 1 Sep 2026 (factor 0.2). */
export const buildCorporateAction = (overrides: Partial<CorporateAction> = {}): CorporateAction => {
  const base: CorporateAction = {
    id: actionId(1),
    instrumentId: dataRowId(4),
    symbol: 'NSE:INFY',
    name: 'INFOSYS LTD',
    exDate: '2026-09-01',
    kind: 'split',
    numerator: 1,
    denominator: 5,
    ratio: '1:5',
    factor: 0.2,
    status: 'suggested',
    source: 'detected',
    note: 'Open fell 80% on 4.2 times the usual volume.',
    createdAt: '2026-10-12T15:55:00+05:30',
    decidedAt: null,
    decidedBy: null,
    ...overrides,
  };
  return {
    ...base,
    ratio: `${String(base.numerator)}:${String(base.denominator)}`,
    factor: overrides.factor ?? factorFor(base.kind, base.numerator, base.denominator),
  };
};

const decided = { decidedAt: '2026-10-12T16:30:00+05:30', decidedBy: 'owner@example.com' } as const;

/**
 * Five actions: two suggestions (Infosys split 1:5, TCS bonus 1:1), two applied (Reliance bonus 1:1 the day before the
 * newest sample bars, HDFC Bank bonus 1:1 of 2025) and one ignored (Infosys bonus 1:2).
 */
export const sampleCorporateActions: readonly CorporateAction[] = [
  buildCorporateAction(),
  buildCorporateAction({
    id: actionId(2),
    instrumentId: dataRowId(2),
    symbol: 'NSE:TCS',
    name: 'TATA CONSULTANCY SERVICES LTD',
    exDate: '2026-08-10',
    kind: 'bonus',
    numerator: 1,
    denominator: 1,
    note: 'Open fell 50% on 3.1 times the usual volume.',
  }),
  buildCorporateAction({
    id: actionId(3),
    instrumentId: dataRowId(1),
    symbol: 'NSE:RELIANCE',
    name: 'RELIANCE INDUSTRIES LTD',
    exDate: '2026-10-11',
    kind: 'bonus',
    numerator: 1,
    denominator: 1,
    status: 'applied',
    source: 'manual',
    note: 'Bonus 1:1',
    ...decided,
  }),
  buildCorporateAction({
    id: actionId(4),
    instrumentId: dataRowId(3),
    symbol: 'NSE:HDFCBANK',
    name: 'HDFC BANK LTD',
    exDate: '2025-08-26',
    kind: 'bonus',
    numerator: 1,
    denominator: 1,
    status: 'applied',
    source: 'csv',
    note: null,
    ...decided,
  }),
  buildCorporateAction({
    id: actionId(5),
    exDate: '2026-03-02',
    kind: 'bonus',
    numerator: 1,
    denominator: 2,
    status: 'ignored',
    note: 'Open fell 33% on 2.0 times the usual volume.',
    ...decided,
  }),
];

/** The actions endpoint over an in-memory list: status, instrumentId and paging, newest ex-date first. */
export function searchActions(items: readonly CorporateAction[], params: URLSearchParams) {
  const status = params.get('status');
  const instrumentId = params.get('instrumentId');
  const page = Number(params.get('page') ?? 1);
  const pageSize = Number(params.get('pageSize') ?? 50);
  const matches = items
    .filter((a) => !status || a.status === status)
    .filter((a) => !instrumentId || a.instrumentId === instrumentId)
    .sort((a, b) => b.exDate.localeCompare(a.exDate) || a.symbol.localeCompare(b.symbol));
  return {
    items: matches.slice((page - 1) * pageSize, page * pageSize),
    page,
    pageSize,
    total: matches.length,
    pending: items.filter((a) => a.status === 'suggested').length,
  };
}

/** The sample coverage with the CSV-only instruments ending on 30 Apr 2026, as the owner's files do. */
export const coverageWithCsvEnd = (lastBar: string = csvEndBar): DataCoverageRow[] =>
  sampleCoverage.map((row) => (row.source === 'csv' && row.lastBar ? { ...row, lastBar } : row));

export const buildActionsNotice = (overrides: Partial<DataNotice> = {}): DataNotice => ({
  id: 'data:corporate_actions_pending:2',
  kind: 'data_corporate_actions_pending',
  severity: 'warn',
  text: '2 suggested corporate actions wait for your decision',
  at: '2026-10-12T15:55:00+05:30',
  ...overrides,
});

export const buildQualityNotice = (overrides: Partial<DataNotice> = {}): DataNotice => ({
  id: 'data:quality_failed:1760266320:3',
  kind: 'data_quality_failed',
  severity: 'error',
  text: '3 instruments fail the data quality checks; the nightly plan is blocked',
  at: '2026-10-12T15:53:00+05:30',
  ...overrides,
});
