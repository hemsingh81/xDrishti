import type { TrainingReportType } from '@/features/reports';
import { isRealDay } from '@/shared/lib/dates';

export interface SimChoice {
  scope: 'all' | 'stock' | 'basket';
  symbol: string;
  basket: string;
  from: string;
  to: string;
  type: TrainingReportType;
}

export const symbolPattern = /^[A-Za-z0-9&_.-]{1,32}$/;

/** The reason a choice cannot be asked yet, else undefined. */
export function choiceProblem(c: SimChoice): string | undefined {
  if (c.scope === 'stock' && !symbolPattern.test(c.symbol)) return 'Enter a stock symbol, for example RELIANCE.';
  if (c.scope === 'basket' && c.basket === '') return 'Choose a basket.';
  if (c.from !== '' && !isRealDay(c.from)) return 'The from date is not a real day.';
  if (c.to !== '' && !isRealDay(c.to)) return 'The to date is not a real day.';
  if (c.from !== '' && c.to !== '' && c.from > c.to) return 'The from date is after the to date.';
  return undefined;
}
