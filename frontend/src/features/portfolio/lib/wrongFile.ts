import { ApiError } from '@/shared/api';
import { problemKind } from './errors';

/** What the API recognised in a file it refused with `portfolio.wrong_file_kind`. */
export type DetectedKind =
  | 'dividends'
  | 'trades'
  | 'tradebook_without_sides'
  | 'statement'
  | 'global_transaction_report'
  | 'dp_charges'
  | 'demat_holding'
  | 'pnl_report';

/** Dhan's downloaded reports: they are imported by "Import files", which tells them apart (P2-17). */
export const reportKinds: readonly DetectedKind[] = [
  'global_transaction_report',
  'dp_charges',
  'demat_holding',
  'pnl_report',
];

export const isReportKind = (kind: DetectedKind): boolean => reportKinds.includes(kind);

/** What the trade import can be told (a dividend list; a tradebook with no side column; a Dhan report). */
export const tradeImportKinds: readonly DetectedKind[] = ['dividends', 'tradebook_without_sides', ...reportKinds];
/** What the dividend import can be told (a tradebook; one with no side column; a statement of amounts; a Dhan report). */
export const dividendImportKinds: readonly DetectedKind[] = [
  'trades',
  'tradebook_without_sides',
  'statement',
  ...reportKinds,
];

const isDetected = (value: unknown, allowed: readonly DetectedKind[]): value is DetectedKind =>
  allowed.some((kind) => kind === value);

/**
 * The kind of file a 400 `portfolio.wrong_file_kind` says it found (`detected`), or null for any other error (and for a kind
 * the import does not know, `allowed`: the generic message of the problem is then shown).
 */
export function detectedKind(error: unknown, allowed: readonly DetectedKind[]): DetectedKind | null {
  if (problemKind(error) !== 'wrong_file_kind' || !(error instanceof ApiError)) return null;
  const detected: unknown = (error.problem as Record<string, unknown> | undefined)?.detected;
  return isDetected(detected, allowed) ? detected : null;
}

/** Why a tradebook with no side column cannot be read (shown by both imports; no redirect helps). */
export const withoutSidesText =
  'This looks like a tradebook, but it has no Buy/Sell column, so a purchase cannot be told from a sale and its trades cannot be read. Export a trade history that has a side column (Dhan’s trade history), or add the trades by hand.';

/** Why a statement of amounts is not read as dividends (it has a date, a name and an amount, and nothing that says dividend). */
export const statementText =
  'This looks like a statement of amounts, not a dividend list: it has a date, a name and an amount, but nothing that says the amounts are dividends. Use a Dividend Paid column (or add a Dividend Per Share column beside the Amount) and try again.';

/** What a Dhan report dropped on the trade or dividend import is, and where it goes. */
export const reportKindText: Record<string, string> = {
  global_transaction_report:
    'This is a Dhan global transaction report: Import files reads its sides, prices and charges as trades.',
  dp_charges: 'This is a depository (DP) charges sheet: Import files stores its charges as costs of the account.',
  demat_holding:
    'This is a demat holding statement: Import files checks it against your holdings and learns the names in it.',
  pnl_report:
    'This is a profit and loss statement: Import files recognises it, and does not import it (it is a statement of results).',
};
