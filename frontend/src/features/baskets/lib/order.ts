/**
 * Moves `moving` (one or several ids) next to `overId`, keeping their relative order. `activeId` is the one the owner
 * grabbed: when it was above the target the block lands after it, otherwise before it (the usual list-sort rule).
 * Returns null when nothing would change (dropped on itself, or on another moved row).
 */
export function moveBlock(
  order: readonly string[],
  moving: readonly string[],
  activeId: string,
  overId: string,
): string[] | null {
  const block = order.filter((id) => moving.includes(id));
  const remaining = order.filter((id) => !moving.includes(id));
  const activeIndex = order.indexOf(activeId);
  const overIndex = order.indexOf(overId);
  const target = remaining.indexOf(overId);
  if (activeIndex < 0 || overIndex < 0 || target < 0 || block.length === 0) return null;
  const at = overIndex > activeIndex ? target + 1 : target;
  const next = [...remaining.slice(0, at), ...block, ...remaining.slice(at)];
  return next.every((id, index) => id === order[index]) ? null : next;
}

/** `wanted` first (only ids that are still in `current`), then whatever else `current` has, in its own order. */
export function mergeOrder(wanted: readonly string[], current: readonly string[]): string[] {
  const present = new Set(current);
  const first = wanted.filter((id) => present.has(id));
  const placed = new Set(first);
  return [...first, ...current.filter((id) => !placed.has(id))];
}

export const sameOrder = (a: readonly string[], b: readonly string[]): boolean =>
  a.length === b.length && a.every((id, index) => id === b[index]);
