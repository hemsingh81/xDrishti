import { useNow } from '../hooks/useNow';
import { formatCountdown } from '../lib/planText';

/** Live countdown to the review cutoff; says "Cutoff passed" afterwards. */
export function Countdown({ cutoff }: { cutoff: string }) {
  const now = useNow();
  return <span>{formatCountdown(Date.parse(cutoff), now)}</span>;
}
