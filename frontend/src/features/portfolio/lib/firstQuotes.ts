import { istDayIso } from '@/shared/lib/format';
import type { PricesChanged } from '@/shared/realtime';

/**
 * Remembers which instruments were seen quoted on which India day. A quote writes the previous close together with its price,
 * and the close changes once a day (with the first quote of the day); the quotes after it carry the same close. The first
 * quote of an instrument in a day is therefore the one that can make holdings read earlier show a wrong day change.
 */
export class FirstQuotes {
  /** The India day of the last quote seen, per instrument (at most the instruments a connection follows). */
  private readonly seen = new Map<string, string>();

  /** The price times (ms) of the quotes in the message that are the first of their instrument on their India day. */
  firstOf(instruments: PricesChanged['instruments']): number[] {
    const first: number[] = [];
    for (const price of instruments) {
      if (price.source !== 'quote') continue;
      const at = Date.parse(price.priceAt);
      if (Number.isNaN(at)) continue;
      const day = istDayIso(new Date(at));
      const known = this.seen.get(price.instrumentId);
      if (known !== undefined && day <= known) continue;
      this.seen.set(price.instrumentId, day);
      first.push(at);
    }
    return first;
  }
}
