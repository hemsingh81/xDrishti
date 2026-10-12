export type BasketPurpose = 'trading' | 'long_term' | 'watch';

/** Purpose → short chip text. */
export const purposeChip: Readonly<Record<BasketPurpose, string>> = {
  trading: 'Trading',
  long_term: 'Long-term',
  watch: 'Watch',
};

export const purposeOptions: readonly { value: BasketPurpose; label: string }[] = [
  { value: 'trading', label: 'Trading (scanned for the plan)' },
  { value: 'long_term', label: 'Long-term (holdings)' },
  { value: 'watch', label: 'Watch only' },
];

export const purposeLabel = (purpose: string): string =>
  purpose in purposeChip ? purposeChip[purpose as BasketPurpose] : purpose;

/** Purpose → the lower-case tag on a basket card, as in the prototype (`trading`, `long-term`, `watch`). */
export const purposeTag = (purpose: string): string => purpose.replace('_', '-');

const typeLabels: Readonly<Record<string, string>> = { share: 'Share', etf: 'ETF', index: 'Index', future: 'Future' };

export const typeLabel = (type: string): string => typeLabels[type] ?? type;
