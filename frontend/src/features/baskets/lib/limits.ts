import { formatNumber } from '@/shared/lib/format';

/** A basket holds at most this many instruments (the API refuses more with `baskets.too_many_members`). */
export const maxBasketMembers = 2000;

/**
 * Why adding `adding` instruments to a basket that holds `current` would be refused, or null when it fits. Checked before
 * anything is sent: a long add goes out in several calls and would otherwise stop half way. `atMost` says the count is an
 * upper bound (some of the instruments may be members already).
 */
export function capProblem(basket: string, current: number, adding: number, atMost = false): string | null {
  const over = current + adding - maxBasketMembers;
  if (over <= 0) return null;
  const count = `${atMost ? 'up to ' : ''}${formatNumber(adding)}`;
  return `${basket} holds ${formatNumber(current)} and a basket holds at most ${formatNumber(maxBasketMembers)} instruments, so ${count} more do not fit. Select ${formatNumber(over)} fewer.`;
}
