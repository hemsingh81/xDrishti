import type {
  BillCandidate,
  BillDetails,
  BillNameView,
  CostImportResult,
  CostUndoResult,
  ImportFilePreview,
  ImportFileResult,
  ImportWarning,
  TradeImportResult,
} from '@/shared/api';
import { formatInrPrecise } from '@/shared/lib/format';
import type { ReportChoice } from '../api/reportUpload';
import { spellingsShown } from './dividendImport';
import { countOf, importedText as tradesImportedText } from './tradeText';

/** The kinds of file the import screen tells apart (the API's `kind`). */
export const kindLabels: Record<string, string> = {
  global_transaction_report: 'Transaction report',
  dp_charges: 'DP charges',
  demat_holding: 'Demat holding',
  pnl_report: 'Profit and loss statement',
  trades: 'Trade list',
  dividends: 'Dividend list',
  tradebook_without_sides: 'Tradebook without buy/sell',
  statement: 'Statement of amounts',
  unknown: 'Unknown file',
};

/** The kind in plain words; a kind this screen does not know (a newer API) is shown as it came. */
export const kindLabel = (kind: string): string => kindLabels[kind] ?? kind;

// ---- What the owner decided for a name -----------------------------------------------------------------------------

/** What a scrip name of a transaction report was decided to mean: a holding, an instrument of the list, or a typed ISIN. */
export type BillTarget =
  | { kind: 'holding'; id: string; label: string }
  | { kind: 'instrument'; id: string; label: string }
  | { kind: 'isin'; isin: string };

/** The decision for one name (it applies to every row with that name, in every file). */
export interface BillDraft {
  /** The server's normalised key of the name (`nameKey`). */
  key: string;
  target: BillTarget;
  /** Save it as a remembered match, so the next file with this name needs no question. */
  remember: boolean;
}

export type BillDrafts = ReadonlyMap<string, BillDraft>;
export const noBillDrafts: BillDrafts = new Map();

/** The owner's decisions about names, shared by every card (a choice applies to every file). */
export interface NameChoices {
  drafts: BillDrafts;
  choicesChanged: boolean;
  onChoose: (draft: BillDraft) => void;
  onUnchoose: (key: string) => void;
  onPreviewAgain: () => void;
}

/** The API's form of a decision. */
export function choiceOfDraft(draft: BillDraft): ReportChoice {
  const { target } = draft;
  const base = { name: draft.key, remember: draft.remember };
  if (target.kind === 'holding') return { ...base, holdingId: target.id };
  if (target.kind === 'instrument') return { ...base, instrumentId: target.id };
  return { ...base, isin: target.isin };
}

/** The decisions as the request sends them, in a stable order (so "did they change?" is a text comparison). */
export const choicesOf = (drafts: BillDrafts): ReportChoice[] =>
  [...drafts.values()].map(choiceOfDraft).sort((a, b) => a.name.localeCompare(b.name));

/** The decision in words: the holding or instrument, or the ISIN typed. */
export const targetText = (target: BillTarget): string =>
  target.kind === 'isin' ? `ISIN ${target.isin}` : target.label;

/** An ISIN is 12 characters: two letters, nine letters or digits and a check digit. */
export const isIsin = (text: string): boolean => /^[A-Z]{2}[A-Z0-9]{9}[0-9]$/.test(text.trim().toUpperCase());

/** The names (by `nameKey`) the previews ask about, so a fresh preview keeps the decisions that still mean something. */
export function keepDrafts(previews: readonly (ImportFilePreview | null)[], drafts: BillDrafts): BillDrafts {
  const keys = new Set(previews.flatMap((preview) => preview?.bill?.names.map((name) => name.nameKey) ?? []));
  return new Map([...drafts].filter(([key]) => keys.has(key)));
}

// ---- The names of a transaction report -----------------------------------------------------------------------------

/** A name nothing fits (or more than one security fits): the owner's question. */
export const isOpenName = (name: BillNameView): boolean => name.status !== 'resolved';

/** The rows with one name key, as one line of the "needs a match" table. */
export interface NameGroup {
  key: string;
  /** The distinct spellings in the file, in file order. */
  spellings: string[];
  symbolHint: string | null;
  rows: number;
  trades: number;
  candidates: BillCandidate[];
}

/** The open names grouped by name key (a decision belongs to a name, however it is spelt), in file order. */
export function openNameGroups(bill: BillDetails): NameGroup[] {
  const groups = new Map<string, NameGroup>();
  for (const name of bill.names) {
    if (!isOpenName(name)) continue;
    const group = groups.get(name.nameKey) ?? {
      key: name.nameKey,
      spellings: [],
      symbolHint: name.symbolHint,
      rows: 0,
      trades: 0,
      candidates: [],
    };
    if (!group.spellings.includes(name.name)) group.spellings.push(name.name);
    group.rows += name.rows;
    group.trades += name.trades;
    if (group.candidates.length === 0) group.candidates = name.candidates;
    groups.set(name.nameKey, group);
  }
  return [...groups.values()];
}

export const resolvedNames = (bill: BillDetails): BillNameView[] => bill.names.filter((name) => !isOpenName(name));

/** The spellings of a name as one line: up to three, then how many more. */
export function spellingsLine(spellings: readonly string[]): string {
  const more = spellings.length - spellingsShown;
  const listed = spellings.slice(0, spellingsShown).join(', ');
  return more > 0 ? `${listed} +${String(more)} more` : listed;
}

/** How a name was matched (the server's `kind`), in words. */
export const nameKindLabels: Record<string, string> = {
  choice: 'your choice',
  alias: 'remembered match',
  symbol: 'symbol',
  holding: 'your holding',
  master: 'instrument list',
  learned: 'learned from a file',
  partial: 'part of the name: check it',
  none: 'no match',
};

// ---- Warnings, skipped rows, statuses ------------------------------------------------------------------------------

/** A notice about a file in plain words (the server's own text is shown only for a code this screen does not know). */
export function warningText(warning: ImportWarning, accountName: string): string {
  const count = warning.count ?? 0;
  switch (warning.code) {
    case 'other_account':
      return `This file looks like it belongs to another Dhan account, not ${accountName}.`;
    case 'bad_rows':
      return `${countOf(count, 'row')} could not be read and ${count === 1 ? 'is' : 'are'} left out.`;
    case 'needs_match':
      return `${countOf(count, 'name')} could not be tied to a security. Their trades are kept without an ISIN until you choose a match.`;
    case 'partial_name_match':
      return `${countOf(count, 'name')} ${count === 1 ? 'was' : 'were'} matched by part of the name: check ${count === 1 ? 'it' : 'them'}.`;
    case 'charges_differ_from_footer':
      return 'The charges of the rows add up to a different amount than the report’s own total.';
    case 'negative_charges':
      return 'Some rows have negative charges (a refund). They are kept as they are.';
    case 'value_not_reproduced':
      return 'On some rows quantity × price does not reproduce the value in the report.';
    case 'covered_by_dhan':
      return `${countOf(count, 'trade')} ${count === 1 ? 'is' : 'are'} left out because Dhan’s own trade history already has the same day, security and side.`;
    case 'totals_differ':
      return 'On some days the report’s quantity differs from Dhan’s own trade history. Dhan’s is used; the differences are listed below.';
    case 'holdings_differ':
      return 'The statement and your synced holdings differ. Nothing is changed; the differences are listed below.';
    case 'notice_only':
      return 'This file is recognised but not imported.';
    default:
      return warning.message;
  }
}

/** Notices that are good news or plain information rather than something to act on. */
export const informationalWarnings: readonly string[] = ['covered_by_dhan', 'notice_only', 'negative_charges'];

/** The reasons rows were left out, in words (extends the trade import's). */
export const skipReasonLabels: Record<string, string> = {
  derivatives: 'Futures and options',
  commodity: 'Commodity (MCX)',
  currency: 'Currency',
  bse: 'BSE only',
  non_equity: 'Not equity',
  covered_by_dhan: 'Covered by Dhan',
  unresolved: 'Security not found',
};

export const skipReasonLabel = (reason: string): string => skipReasonLabels[reason] ?? reason;

/** What happens to a depository-charges line. */
export const costStatusLabels: Record<string, string> = {
  new: 'Will be stored',
  zero: 'Costs nothing',
  buy_side: 'Buy side: not charged',
  already_imported: 'Already imported',
  already_in_ledger: 'Already in the ledger',
};

export const costStatusLabel = (status: string): string => costStatusLabels[status] ?? status;

export const sideLabel = (side: string): string => (side === 'buy' ? 'Buy' : side === 'sell' ? 'Sell' : 'Other');

// ---- What a file can do --------------------------------------------------------------------------------------------

export const isTradeKind = (kind: string): boolean => kind === 'global_transaction_report' || kind === 'trades';

/** Whether importing the file would store or learn something (a file that is stored already, or cannot be imported, is not). */
export function willChange(preview: ImportFilePreview): boolean {
  if (!preview.supported || preview.error !== null) return false;
  if (preview.trades) return preview.trades.file.newTrades > 0;
  if (preview.costs) return preview.costs.counts.toStore > 0;
  if (preview.demat) return preview.demat.names.new > 0;
  return false;
}

export const hasOtherAccountWarning = (preview: ImportFilePreview): boolean =>
  preview.warnings.some((warning) => warning.code === 'other_account');

// ---- Messages ------------------------------------------------------------------------------------------------------

/** The line of a result about the trades. */
export function tradesLine(result: TradeImportResult): string {
  if (result.batch === null) return `The ${countOf(result.alreadyImported, 'trade')} of the file were stored already.`;
  const already = result.alreadyImported > 0 ? `, ${countOf(result.alreadyImported, 'trade')} were stored already` : '';
  const lots = result.rebuilt
    ? ` The lots of ${countOf(result.holdingsRebuilt, 'holding')} were rebuilt (${countOf(result.lotsReplaced, 'lot')} replaced).`
    : ' Your lots were not changed.';
  return `${countOf(result.imported, 'trade')} stored${already}.${lots}`;
}

/** The line of a result about the depository charges. */
export function costsLine(result: CostImportResult): string {
  if (result.batch === null) {
    return `Nothing new: ${countOf(result.alreadyImported, 'line')} imported already and ${countOf(result.alreadyInLedger, 'line')} in the ledger already.`;
  }
  const left = [
    result.alreadyImported > 0 ? `${countOf(result.alreadyImported, 'line')} imported already` : null,
    result.alreadyInLedger > 0 ? `${countOf(result.alreadyInLedger, 'line')} in the ledger already` : null,
  ].filter((part): part is string => part !== null);
  return `${countOf(result.imported, 'charge')} stored (${formatInrPrecise(result.amount)})${left.length > 0 ? `; ${left.join(', ')}` : ''}.`;
}

/** One line for the toast after an import: what each kind of file did, and the files that were not imported. */
export function importSummaryText(results: readonly ImportFileResult[]): string {
  const imported = results.filter((result) => result.status === 'imported').length;
  const parts = [
    ...results.flatMap((result) => (result.trades?.batch ? [tradesImportedText(result.trades)] : [])),
    ...results.flatMap((result) => (result.costs?.batch ? [`Imported ${costsLine(result.costs)}`] : [])),
  ];
  const nothing = results.filter((result) => result.status === 'nothing_new').length;
  const rejected = results.filter((result) => result.status === 'rejected' || result.status === 'not_supported').length;
  const tail = [
    nothing > 0 ? `${countOf(nothing, 'file')} had nothing new` : null,
    rejected > 0 ? `${countOf(rejected, 'file')} not imported` : null,
  ].filter((part): part is string => part !== null);
  if (imported === 0 && tail.length === 0) return 'Nothing was imported.';
  return [...parts, ...tail.map((part) => `${part}.`)].join(' ') || `Imported ${countOf(imported, 'file')}.`;
}

/** The toast after an undo of a cost import. */
export function costUndoneText(result: CostUndoResult): string {
  return result.alreadyUndone
    ? 'That import was undone already. Nothing changed.'
    : `Import undone: ${countOf(result.voided, 'charge')} voided.`;
}
