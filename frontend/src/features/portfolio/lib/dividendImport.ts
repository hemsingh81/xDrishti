import type {
  DividendCandidate,
  DividendChoice,
  DividendImportResult,
  DividendMatch,
  DividendPreviewItem,
  DividendPreviewReport,
  DividendUndoResult,
} from '@/shared/api';
import { formatInrPrecise } from '@/shared/lib/format';
import { countOf } from './tradeText';

/** Rows of a table shown at a time: a file of 20,000 rows is read page by page, never as 20,000 table rows. */
export const importPageSize = 50;

/** Where the owner sent the dividends of a name: one of the account's holdings, an instrument of the list, or nowhere. */
export type MatchTarget =
  { kind: 'holding'; id: string; label: string } | { kind: 'instrument'; id: string; label: string } | { kind: 'none' };

/** What the owner decided for one scrip name of the file (it applies to every row with that name). */
export interface MatchDraft {
  /** The server's normalised key of the name (`nameKey`): spellings that differ only in case or punctuation share it. */
  key: string;
  target: MatchTarget;
  /** Save it as an alias, so the next file with this name needs no question. */
  remember: boolean;
}

/** The decisions, keyed by the name key (`nameKey`) the server gave the rows. */
export type Drafts = ReadonlyMap<string, MatchDraft>;

export const noDrafts: Drafts = new Map();

/** The target a picker option stands for: its value is `h:<holding id>`, `i:<instrument id>` or `none`. */
export function targetOfOption(option: { value: string; label: string }): MatchTarget {
  if (option.value.startsWith('h:')) return { kind: 'holding', id: option.value.slice(2), label: option.label };
  if (option.value.startsWith('i:')) return { kind: 'instrument', id: option.value.slice(2), label: option.label };
  return { kind: 'none' };
}

/** The picker option (value and label) that stands for a target. */
export function optionOfTarget(target: MatchTarget): { value: string; label: string } {
  if (target.kind === 'none') return { value: 'none', label: 'Leave unallocated' };
  return { value: `${target.kind === 'holding' ? 'h' : 'i'}:${target.id}`, label: target.label };
}

/** The API's form of a decision. */
export function choiceOf(draft: MatchDraft): DividendChoice {
  const { target } = draft;
  return {
    name: draft.key,
    holdingId: target.kind === 'holding' ? target.id : null,
    instrumentId: target.kind === 'instrument' ? target.id : null,
    none: target.kind === 'none',
    remember: draft.remember,
  };
}

/** Decisions and ticked duplicates that still mean something in a (fresh) preview: the rest are dropped. */
export function keepForReport(
  report: DividendPreviewReport,
  drafts: Drafts,
  lines: ReadonlySet<number>,
): { drafts: Drafts; lines: ReadonlySet<number> } {
  const keys = new Set(report.items.map((item) => item.nameKey));
  const duplicates = new Set(report.items.filter((item) => item.status === 'possible_duplicate').map((i) => i.line));
  return {
    drafts: new Map([...drafts].filter(([key]) => keys.has(key))),
    lines: new Set([...lines].filter((line) => duplicates.has(line))),
  };
}

export const matchKindLabels: Record<string, string> = {
  alias: 'remembered match',
  isin: 'ISIN',
  symbol: 'symbol',
  name: 'same name',
  contains: 'part of the name',
  master: 'instrument list',
  choice: 'your choice',
  none: 'no match',
};

export const warningLabels: Record<string, string> = {
  amount_mismatch: 'Per share × shares differs from the amount paid; the amount paid is used.',
  before_first_lot: 'Dated before your first lot.',
  partial_name_match: 'Matched by part of the name: check it.',
};

export const warningText = (warning: string): string => warningLabels[warning] ?? warning;

/** The symbol of an instrument without its exchange ("NSE:TCS" -> "TCS"). */
const bare = (symbol: string): string => symbol.split(':').at(-1) ?? symbol;

/** What a match says in a few words: the symbol (or name) of the holding or instrument; null when there is none. */
export function matchTitle(match: DividendMatch): string | null {
  if (match.holdingId === null && match.instrumentId === null) return null;
  return match.symbol === null ? match.name : bare(match.symbol);
}

export const matchesHolding = (match: DividendMatch): boolean => match.holdingId !== null;

/** Whether the row is an open question the owner may answer (a name nothing fits, or more than one holding fits). */
export const isOpen = (item: DividendPreviewItem): boolean =>
  item.status === 'needs_match' || item.status === 'ambiguous';

/** The rows with the name, as one line of the "needs a match" table. */
export interface NameGroup {
  /** The server's normalised name key: what the decision is kept under. */
  key: string;
  /** The distinct spellings of the name in the file, in file order. */
  spellings: string[];
  rows: number;
  amount: number;
  candidates: DividendCandidate[];
}

/** The open rows grouped by name key (the decision belongs to a name, however it is spelt), in file order. */
export function openGroups(items: readonly DividendPreviewItem[]): NameGroup[] {
  const groups = new Map<string, NameGroup>();
  for (const item of items) {
    if (!isOpen(item)) continue;
    const group = groups.get(item.nameKey) ?? {
      key: item.nameKey,
      spellings: [],
      rows: 0,
      amount: 0,
      candidates: [],
    };
    if (!group.spellings.includes(item.name)) group.spellings.push(item.name);
    group.rows += 1;
    group.amount += item.amount;
    if (group.candidates.length === 0) group.candidates = item.match.candidates;
    groups.set(item.nameKey, group);
  }
  return [...groups.values()];
}

/** How many spellings of a name are listed before "+n more". */
export const spellingsShown = 3;

/** The spellings of a name as one line: up to three, then how many more. */
export function spellingsText(spellings: readonly string[]): string {
  const more = spellings.length - spellingsShown;
  const listed = spellings.slice(0, spellingsShown).join(', ');
  return more > 0 ? `${listed} +${String(more)} more` : listed;
}

export function targetText(target: MatchTarget): string {
  return target.kind === 'none' ? 'Left unallocated' : target.label;
}

/** The toast after an import. */
export function importedText(result: DividendImportResult): string {
  return result.batch === null
    ? 'Nothing new to import: every dividend of the file was stored already.'
    : `Imported ${countOf(result.imported, 'dividend')} (${formatInrPrecise(result.amount)}).`;
}

/** The toast after an undo. */
export function undoneText(result: DividendUndoResult): string {
  return result.alreadyUndone
    ? 'That import was undone already. Nothing changed.'
    : `Import undone: ${countOf(result.voided, 'dividend')} voided.`;
}

/** The sentence under the Import button: what will be stored. */
export function willImportText(count: number, amount: number): string {
  return `${countOf(count, 'dividend')} (${formatInrPrecise(amount)}) will be stored.`;
}
