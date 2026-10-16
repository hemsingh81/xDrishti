import { FirstQuotes } from './firstQuotes';

const quote = (id: string, priceAt: string, source = 'quote') => ({ instrumentId: id, lastPrice: 10, priceAt, source });
const mon = '2026-10-12T04:00:00Z';
const tue = '2026-10-13T04:00:00Z';

describe('FirstQuotes', () => {
  it('reports the first quote of an instrument in a day, not the ones after it', () => {
    const quotes = new FirstQuotes();

    expect(quotes.firstOf([quote('a', mon), quote('b', mon)])).toHaveLength(2);
    expect(quotes.firstOf([quote('a', '2026-10-12T04:05:00Z')])).toEqual([]);
    expect(quotes.firstOf([quote('c', '2026-10-12T04:05:00Z')])).toHaveLength(1);
  });

  it('forgets everything when the India day changes', () => {
    const quotes = new FirstQuotes();
    quotes.firstOf([quote('a', mon)]);

    expect(quotes.firstOf([quote('a', tue)])).toEqual([Date.parse(tue)]);
    expect(quotes.firstOf([quote('a', '2026-10-13T04:05:00Z')])).toEqual([]);
  });

  it('ignores live and sync prices, and a live tick in between does not make the next quote a first', () => {
    const quotes = new FirstQuotes();

    expect(quotes.firstOf([quote('a', mon, 'live'), quote('a', mon, 'sync')])).toEqual([]);
    expect(quotes.firstOf([quote('a', mon)])).toHaveLength(1);
    quotes.firstOf([quote('a', '2026-10-12T04:01:00Z', 'live')]);
    expect(quotes.firstOf([quote('a', '2026-10-12T04:05:00Z')])).toEqual([]);
  });
});
