import type { MetricState } from '@/shared/analytics';
import type { OverviewSection } from '../api/learningQueries';

/** A count of stored records: none is a word, a count that may be cut at the server's limit says so. */
export function countState(section: OverviewSection, noun: string): MetricState {
  if (section.counted === 0) return { kind: 'empty', reason: `No ${noun} stored yet` };
  if (section.truncated) {
    return { kind: 'provisional', value: section.counted, reason: 'Only the newest are counted; there may be more' };
  }
  return { kind: 'value', value: section.counted };
}
