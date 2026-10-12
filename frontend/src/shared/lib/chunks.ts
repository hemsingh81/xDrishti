/** The API takes at most 500 ids per track / untrack / basket call. */
export const maxIdsPerCall = 500;

/** Splits `items` into consecutive slices of at most `size`. */
export function chunk<T>(items: readonly T[], size: number = maxIdsPerCall): T[][] {
  const slices: T[][] = [];
  for (let start = 0; start < items.length; start += size) slices.push(items.slice(start, start + size));
  return slices;
}
