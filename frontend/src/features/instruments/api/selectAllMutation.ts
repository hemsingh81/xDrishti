import { useMutation, useQueryClient } from '@tanstack/react-query';
import { searchQuery } from './instrumentsQueries';
import type { InstrumentFilters } from '../lib/filters';

const pageSize = 100;
/** Pages fetched at the same time. */
const parallel = 6;

/**
 * The ids of every instrument that matches `filters` ("select all N results"): the pages of 100 are read, a few at a
 * time, through the query cache. Sorted by symbol, whatever the list is sorted by, so the pages do not shift.
 */
export function useSelectAllIds() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ filters, total }: { filters: InstrumentFilters; total: number }): Promise<string[]> => {
      const base: InstrumentFilters = { ...filters, sort: { field: 'symbol', descending: false }, pageSize };
      const pages = Array.from({ length: Math.ceil(total / pageSize) }, (_, index) => index + 1);
      const ids: string[] = [];
      for (let start = 0; start < pages.length; start += parallel) {
        const batch = await Promise.all(
          pages
            .slice(start, start + parallel)
            .map((page) => queryClient.query({ ...searchQuery({ ...base, page }), staleTime: 0 })),
        );
        for (const result of batch) ids.push(...result.items.map((item) => item.id));
      }
      return ids;
    },
  });
}
