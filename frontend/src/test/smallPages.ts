import type * as Rows from '@/shared/lib/useInfiniteRows';
import type { InfiniteRowsOptions, PageResult } from '@/shared/lib/useInfiniteRows';

/**
 * Infinite lists ask for 100 rows a page in the app; rendering 100-250 table rows per test is what makes list tests slow
 * in jsdom. A list test uses this module to page by a few rows instead, which proves the same thing (the next page is
 * asked at the end of the list, the new list starts from its first page, ...). The default of 100 is asserted once, in
 * `shared/lib/useInfiniteRows.test.tsx`.
 *
 * In the test file (the factory must stay inside `vi.mock`, which is hoisted):
 *
 *     vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
 *       (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
 *     );
 */
export const smallPage = 10;

/** Rows for a list of three pages, the last one partly filled: `smallPage * 2.5`. */
export const threePages = smallPage * 2 + smallPage / 2;

type Original = Omit<typeof Rows, 'defaultInfinitePageSize'> & { defaultInfinitePageSize: number };

export function withSmallPages(original: Original): Original {
  return {
    ...original,
    defaultInfinitePageSize: smallPage,
    useInfiniteRows: <T, Page extends PageResult<T> = PageResult<T>>(options: InfiniteRowsOptions<T, Page>) =>
      original.useInfiniteRows({ ...options, pageSize: smallPage }),
  };
}
