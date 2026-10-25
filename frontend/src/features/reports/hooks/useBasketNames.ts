import { useQuery } from '@tanstack/react-query';
import { reportBasketsQuery } from '../api/reportQueries';

export const NOT_RECORDED = 'Not recorded';

/** Basket name by id (names from `GET /api/baskets`; an id with no name is shown as the id). */
export function useBasketNames() {
  const baskets = useQuery(reportBasketsQuery);
  const names = new Map((baskets.data ?? []).map((basket) => [basket.id, basket.name]));
  return {
    options: baskets.data ?? [],
    name: (id: string) => names.get(id) ?? id,
    /** A group coordinate as text: a basket id becomes its name, a missing value says so. */
    part: (dimension: string, value: string | null) =>
      value === null ? NOT_RECORDED : dimension === 'basket' ? (names.get(value) ?? value) : value,
  };
}
