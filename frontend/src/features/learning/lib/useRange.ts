import { useUrlState } from '@/shared/analytics';
import type { DateRange } from '../api/learningQueries';

const day = /^\d{4}-\d{2}-\d{2}$/;

/** The `from` / `to` of the address (an unreadable value is an open end), and a setter that keeps the rest of the address. */
export function useRange() {
  const url = useUrlState();
  const read = (name: string) => {
    const found = url.text(name);
    return found !== null && day.test(found) ? found : null;
  };
  const range: DateRange = { from: read('from'), to: read('to') };
  const setRange = (next: DateRange) => {
    url.update({ from: next.from, to: next.to });
  };
  return { range, setRange };
}
