import type { Trade, TradeImportResult, TradeRebuild, TradeSecurity, TradeUndoResult } from '@/shared/api';
import { formatDate, formatInr, formatNumber } from '@/shared/lib/format';

/** "1 trade", "12 trades". */
export const countOf = (n: number, one: string, many = `${one}s`): string =>
  `${formatNumber(n)} ${n === 1 ? one : many}`;

export type RebuildStatus = 'ok' | 'unchanged' | 'quantity_mismatch' | 'no_holding' | 'ambiguous';

export const rebuildStatuses: readonly RebuildStatus[] = [
  'ok',
  'unchanged',
  'quantity_mismatch',
  'no_holding',
  'ambiguous',
];

export const rebuildStatusLabels: Record<RebuildStatus, string> = {
  ok: 'OK',
  unchanged: 'Unchanged',
  quantity_mismatch: 'Quantity mismatch',
  no_holding: 'Not held',
  ambiguous: 'Ambiguous',
};

/** What each status means, for the line under the securities table. */
export const rebuildStatusMeaning =
  'OK: the lots would be rebuilt from the trades. Unchanged: they already match. Quantity mismatch and Ambiguous: nothing is applied. Not held: the trades are stored only.';

export const isRebuildStatus = (value: string): value is RebuildStatus => rebuildStatuses.some((s) => s === value);

/** The status of a rebuild; a value this screen does not know is shown as a mismatch (nothing is applied for it). */
export const statusOf = (rebuild: TradeRebuild): RebuildStatus =>
  isRebuildStatus(rebuild.status) ? rebuild.status : 'quantity_mismatch';

/** Why a rebuild is not applied, in words, with the numbers the API gives; null when there is nothing to explain. */
export function reasonText(rebuild: TradeRebuild): string | null {
  const status = statusOf(rebuild);
  if (status === 'ambiguous') {
    return 'More than one holding of the account matches this security, so nothing is applied.';
  }
  if (status === 'no_holding')
    return 'The account does not hold this security now: its trades are stored, not applied.';
  if (status !== 'quantity_mismatch') return null;
  const numbers =
    rebuild.q !== null && rebuild.net !== null
      ? ` Dhan shows ${formatNumber(rebuild.q)} shares; the trades add up to a net ${formatNumber(rebuild.net)}.`
      : '';
  switch (rebuild.reason) {
    case 'negative_opening':
      return `The trades explain more shares than Dhan says you hold (or a buy is missing).${numbers} Nothing is applied.`;
    case 'sell_exceeds_open':
      return `A sell is larger than the shares held at that point (an earlier buy is missing).${numbers} Nothing is applied.`;
    case 'fractional_after_adjustment':
      return `After a split or bonus the quantity is not a whole number.${numbers} Nothing is applied.`;
    default:
      return `The trades do not match what Dhan says you hold.${numbers} Nothing is applied.`;
  }
}

/** "3 hand-dated lots and 2 lots from syncs will be replaced"; null when no such lot is replaced. */
export function replacedSentence(handDated: number, sync: number): string | null {
  const parts = [
    handDated > 0 ? countOf(handDated, 'hand-dated lot') : null,
    sync > 0 ? `${countOf(sync, 'lot')} from syncs` : null,
  ].filter((part): part is string => part !== null);
  return parts.length === 0 ? null : `${parts.join(' and ')} will be replaced`;
}

export interface RebuildTotals {
  holdings: number;
  lotsBefore: number;
  lotsAfter: number;
  handDated: number;
  sync: number;
  adjusted: number;
}

/** What importing with a rebuild would do overall: the holdings whose result is OK, their lots now and after. */
export function rebuildTotals(securities: readonly TradeSecurity[]): RebuildTotals {
  const totals: RebuildTotals = { holdings: 0, lotsBefore: 0, lotsAfter: 0, handDated: 0, sync: 0, adjusted: 0 };
  for (const { rebuild } of securities) {
    if (statusOf(rebuild) !== 'ok') continue;
    totals.holdings += 1;
    totals.lotsBefore += rebuild.lotsBefore;
    totals.lotsAfter += rebuild.lotsAfter;
    totals.handDated += rebuild.handDatedLotsReplaced;
    totals.sync += rebuild.syncLotsReplaced;
    totals.adjusted += rebuild.adjustedFor;
  }
  return totals;
}

/** The toast after an import. */
export function importedText(result: TradeImportResult): string {
  const already = result.alreadyImported > 0 ? ` (${formatNumber(result.alreadyImported)} were stored already)` : '';
  if (result.batch === null) {
    return `Nothing new to import: the ${countOf(result.alreadyImported, 'trade')} in the file were stored already.`;
  }
  const stored = `Imported ${countOf(result.imported, 'trade')}${already}.`;
  return result.rebuilt
    ? `${stored} Rebuilt ${countOf(result.holdingsRebuilt, 'holding')}, ${countOf(result.lotsReplaced, 'lot')} replaced.`
    : `${stored} Your lots were not changed.`;
}

/** The cost of the open lots after a rebuild differs from the broker's figure by at least a rupee (below that is rounding). */
export const costDiffers = (rebuild: TradeRebuild): boolean =>
  rebuild.costDelta !== null && rebuild.costDelta !== undefined && Math.abs(rebuild.costDelta) >= 1;

/**
 * "Cost basis from your trades is ₹1,234 lower than the broker's cost": the lots built from the trades against the broker's
 * quantity × average cost. A negative `costDelta` is lower, a positive one higher. Null when the two agree.
 */
export function costDeltaText(rebuild: TradeRebuild): string | null {
  const delta = rebuild.costDelta;
  if (delta === null || delta === undefined || !costDiffers(rebuild)) return null;
  return `Cost basis from your trades is ${formatInr(Math.abs(delta))} ${delta < 0 ? 'lower' : 'higher'} than the broker’s cost`;
}

/**
 * Why the two costs differ, by case. With an opening lot (the file does not explain the whole holding) the opening lot takes what
 * the trades leave over, so only sales can make a difference: they are costed first-in first-out, a broker's average does not
 * move with a sale. Without an opening lot the cost is the real buy prices of the file against the broker's average (and, after
 * sales, first-in first-out). `sells` is how many sells the file has for the security when it is known; else realised profit tells.
 */
export function costWhy(rebuild: TradeRebuild, sells?: number): string {
  const opening = (rebuild.openingQty.after ?? 0) > 0;
  const hasSells = (sells ?? 0) > 0 || (rebuild.realisedPnl !== null && rebuild.realisedPnl !== 0);
  if (opening && hasSells) {
    return 'The lots built from your trades are costed first-in first-out. A broker keeps a weighted average cost that a sale does not change, so after sales the two differ. The broker’s quantity and cost stay the ones the app uses.';
  }
  if (!opening) {
    return `Your trades explain the whole holding, so its cost comes from the real buy prices in the file, which differ from your broker’s average cost${hasSells ? ' (and sales are costed first-in first-out)' : ''}. The broker’s quantity and cost stay the ones the app uses.`;
  }
  return 'The cost of the lots built from your trades differs from your broker’s figure. The broker’s quantity and cost stay the ones the app uses.';
}

/** Why the opening lot costs the broker's average cost instead of what the trades leave over; null when it does not. */
export const costNoteText = (rebuild: TradeRebuild): string | null =>
  rebuild.costNote === 'opening_cost_fallback'
    ? 'the opening lot’s cost could not be derived from the trades, so the broker’s average cost was used.'
    : null;

/** Why an undo could not rebuild a holding, in words (the API's reason: ambiguous or a mismatch). */
export function notRebuiltReason(reason: string): string {
  switch (reason) {
    case 'ambiguous':
      return 'more than one holding matches';
    case 'negative_opening':
      return 'the remaining trades explain more shares than Dhan says you hold';
    case 'sell_exceeds_open':
      return 'a sell is larger than the shares held at that point';
    case 'fractional_after_adjustment':
      return 'a split or bonus leaves a fraction of a share';
    default:
      return 'the trades do not match what Dhan says you hold';
  }
}

/** The toast after an undo. */
export function undoneText(result: TradeUndoResult): string {
  if (result.alreadyUndone) return `“${result.batch.fileName}” had already been undone. Nothing changed.`;
  const rebuilt =
    result.holdingsRebuilt > 0
      ? ` Rebuilt ${countOf(result.holdingsRebuilt, 'holding')}, ${countOf(result.lotsReplaced, 'lot')} replaced.`
      : ' No lots were changed.';
  const names = result.notRebuilt.map((entry) => entry.symbol).join(', ');
  const kept =
    result.holdingsNotRebuilt > 0
      ? ` ${countOf(result.holdingsNotRebuilt, 'holding')} could not be rebuilt and kept their lots${names ? ` (${names})` : ''}.`
      : '';
  return `Undid “${result.batch.fileName}”: ${countOf(result.voided, 'trade')} voided.${rebuilt}${kept}`;
}

export const skippedLabels: Record<string, string> = {
  derivatives: 'Derivatives',
  commodity: 'Commodity',
  currency: 'Currency',
  bse: 'BSE only',
  non_equity: 'Not equity',
};

/** The reasons trades were left out with their counts, biggest first; unknown reasons are shown under their own name. */
export function skippedParts(
  byReason: Readonly<Record<string, number>>,
): { reason: string; label: string; count: number }[] {
  return Object.entries(byReason)
    .filter(([, count]) => count > 0)
    .map(([reason, count]) => ({ reason, label: skippedLabels[reason] ?? reason, count }))
    .sort((a, b) => b.count - a.count || a.label.localeCompare(b.label));
}

/** A trade in words, for a button's name and a message: "buy of 10 INFY on 5 Oct 2026". */
export const tradeName = (trade: Trade): string =>
  `${trade.side} of ${formatNumber(trade.qty)} ${trade.symbol} on ${formatDate(trade.tradeDate)}`;
