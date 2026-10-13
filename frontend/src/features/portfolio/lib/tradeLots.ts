import type { LotRow } from '@/shared/api';

/**
 * Whether a holding's lots were built from trades, as the API decides it (`Holding.HasTradeLots`): some lot has the source
 * `trade`, or some lot was closed at a real sale price (not an estimate) — a holding rebuilt from sells alone has only those.
 */
export const lotsFromTrades = (lots: readonly LotRow[]): boolean =>
  lots.some((lot) => lot.source === 'trade' || (lot.status === 'closed' && !lot.estimated));

/**
 * The opening lot of a rebuilt holding: the shares the trades do not explain. A rebuild makes it an open lot with the source
 * `import` (its date stays editable, before the earliest trade); every other lot comes from a trade. Lots of a holding that
 * was not built from trades are never called that.
 */
export const isOpeningLot = (lots: readonly LotRow[], lot: LotRow): boolean =>
  lotsFromTrades(lots) && lot.source === 'import' && lot.status === 'open';
